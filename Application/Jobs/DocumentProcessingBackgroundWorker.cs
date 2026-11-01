using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Application.Orchestration;
using PixousDocIntelligence.Persistence.Repositories;
using PixousDocIntelligence.Services.Accounts;

namespace PixousDocIntelligence.Application.Jobs;

/// <summary>
/// Hosted background service that pulls queued document jobs from IDocumentJobQueue
/// and runs the orchestrator asynchronously, decoupling large (100+ page) document processing
/// from the HTTP request thread.
/// </summary>
public class DocumentProcessingBackgroundWorker : BackgroundService
{
    private readonly IDocumentJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingBackgroundWorker> _logger;
    private readonly ProcessingOptions _options;
    private readonly Random _jitterRandom = new();

    public DocumentProcessingBackgroundWorker(
        IDocumentJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingBackgroundWorker> logger,
        ProcessingOptions options)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    private const int MaxConcurrentJobs = 6;
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentJobs, MaxConcurrentJobs);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentProcessingBackgroundWorker started with {MaxConcurrentJobs} concurrent worker slots.", MaxConcurrentJobs);

        while (!stoppingToken.IsCancellationRequested)
        {
            DocumentJobWorkItem item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Wait for an available concurrency slot (bounded to MaxConcurrentJobs)
            await _concurrencyLimiter.WaitAsync(stoppingToken);

            // Execute job on background thread pool
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessJobAsync(item, stoppingToken);
                }
                finally
                {
                    _concurrencyLimiter.Release();
                }
            }, stoppingToken);
        }

        _logger.LogInformation("DocumentProcessingBackgroundWorker is shutting down.");
    }

    private async Task ProcessJobAsync(DocumentJobWorkItem item, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing job '{JobId}' for document '{DocumentId}' [Campaign: '{CampaignId}'] (attempt {Attempt})",
            item.JobId, item.DocumentId, item.CampaignId ?? "none", item.AttemptCount + 1);

        bool retryScheduled = false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IDocumentProcessingOrchestrator>();
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentProcessingRepository>();
            var credentialRegistry = scope.ServiceProvider.GetRequiredService<PixousDocIntelligence.Credentials.ICredentialRegistry>();
            var accountService = scope.ServiceProvider.GetRequiredService<CustomerAccountService>();
            var campaignStorage = scope.ServiceProvider.GetRequiredService<PixousDocIntelligence.Services.Campaigns.ICampaignStorageService>();

            // Run end-to-end 7-phase pipeline
            var result = await orchestrator.ProcessDocumentAsync(item.Request, cancellationToken: stoppingToken);

            // If job belongs to a batch campaign, persist isolated bid record
            if (!string.IsNullOrWhiteSpace(item.CampaignId))
            {
                string bidId = item.BidId ?? item.JobId;
                string sourceFileName = item.SourceFileName ?? Path.GetFileName(item.Request.DocumentPath);
                await campaignStorage.SaveIsolatedBidResultAsync(item.CampaignId, bidId, result, sourceFileName, stoppingToken);
            }

            // Record usage / credits
            int actualPages = result.Manifest.TotalPages > 0 ? result.Manifest.TotalPages : item.IncomingPages;
            if (item.BilledAccountId != null)
            {
                await accountService.RecordCreditUsageAsync(item.BilledAccountId, actualPages, stoppingToken);
                if (item.ClientId != null)
                {
                    await credentialRegistry.RecordUsageAsync(item.ClientId, actualPages, "document_process_async", stoppingToken);
                }
            }
            else
            {
                await credentialRegistry.RecordUsageAsync(item.ClientIdentifier, actualPages, "document_process_async", stoppingToken);
            }

            _logger.LogInformation("Successfully completed job '{JobId}' in {DurationMs} ms with status '{Status}'",
                item.JobId, result.ExecutionDurationMs, result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background processing failed for job '{JobId}' (attempt {Attempt})", item.JobId, item.AttemptCount + 1);

            bool isRetryableError = IsRetryableException(ex);
            bool retriesRemain = item.AttemptCount < _options.JobRetry.MaxRetries;

            try
            {
                if (isRetryableError && retriesRemain)
                {
                    item.AttemptCount++;
                    TimeSpan delay = ComputeRetryDelay(item.AttemptCount);
                    DateTime nextAttemptAt = DateTime.UtcNow.Add(delay);

                    using var retryScope = _scopeFactory.CreateScope();
                    var retryRepo = retryScope.ServiceProvider.GetRequiredService<IDocumentProcessingRepository>();
                    await retryRepo.MarkJobRetryScheduledAsync(item.JobId, item.AttemptCount, nextAttemptAt, ex.Message, CancellationToken.None);

                    _logger.LogWarning(
                        "Job '{JobId}' hit a transient error — scheduling retry {Attempt}/{MaxRetries} in {DelaySeconds:F0}s.",
                        item.JobId, item.AttemptCount, _options.JobRetry.MaxRetries, delay.TotalSeconds);

                    // Schedule the retry for later instead of blocking this worker's concurrency
                    // slot: this method is about to return, which releases the slot immediately.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(delay, stoppingToken);
                            await _queue.EnqueueAsync(item, CancellationToken.None);
                        }
                        catch (OperationCanceledException)
                        {
                            // App is shutting down; drop the retry rather than enqueue into a dead worker.
                        }
                    }, CancellationToken.None);

                    retryScheduled = true;
                }
                else
                {
                    using var errScope = _scopeFactory.CreateScope();
                    var repo = errScope.ServiceProvider.GetRequiredService<IDocumentProcessingRepository>();
                    await repo.UpdateJobStatusAsync(item.JobId, "Failed", ex.Message, CancellationToken.None);

                    if (!string.IsNullOrWhiteSpace(item.CampaignId))
                    {
                        var campaignStorage = errScope.ServiceProvider.GetRequiredService<PixousDocIntelligence.Services.Campaigns.ICampaignStorageService>();
                        await campaignStorage.MarkBidFailedAsync(item.CampaignId, item.BidId ?? item.JobId, ex.Message, CancellationToken.None);
                    }
                }
            }
            catch (Exception statusEx)
            {
                _logger.LogWarning(statusEx, "Failed to record failure/retry status for job '{JobId}'", item.JobId);
            }
        }
        finally
        {
            // Keep the staged upload around when a retry is pending — the retried attempt needs
            // the same file on disk. Only clean up once the job has reached a terminal state.
            if (!retryScheduled && !string.IsNullOrWhiteSpace(item.TempFilePath) && File.Exists(item.TempFilePath))
            {
                try { File.Delete(item.TempFilePath); } catch { /* ignore lock */ }
            }
        }
    }

    /// <summary>
    /// Distinguishes transient failures (worth retrying the whole job) from permanent ones
    /// (bad input, unsupported format, etc.) that would just fail identically again.
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            TaskCanceledException => true,
            TimeoutException => true,
            IOException => true,
            _ => false
        };
    }

    /// <summary>
    /// Bounded exponential backoff with jitter: BaseDelaySeconds * 2^(attempt-1), capped at
    /// MaxDelaySeconds, randomized to 0.5x-1.5x so many simultaneously-failing jobs don't all
    /// retry at the exact same instant.
    /// </summary>
    private TimeSpan ComputeRetryDelay(int attemptCount)
    {
        double rawDelaySeconds = _options.JobRetry.BaseDelaySeconds * Math.Pow(2, attemptCount - 1);
        double cappedDelaySeconds = Math.Min(rawDelaySeconds, _options.JobRetry.MaxDelaySeconds);
        double jitterFactor = 0.5 + _jitterRandom.NextDouble();
        return TimeSpan.FromSeconds(cappedDelaySeconds * jitterFactor);
    }
}
