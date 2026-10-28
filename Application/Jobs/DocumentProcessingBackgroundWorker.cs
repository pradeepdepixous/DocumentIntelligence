using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    public DocumentProcessingBackgroundWorker(
        IDocumentJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
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
        _logger.LogInformation("Processing job '{JobId}' for document '{DocumentId}' [Campaign: '{CampaignId}']", 
            item.JobId, item.DocumentId, item.CampaignId ?? "none");

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
            _logger.LogError(ex, "Background processing failed for job '{JobId}'", item.JobId);
            try
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
            catch (Exception statusEx)
            {
                _logger.LogWarning(statusEx, "Failed to record failure status for job '{JobId}'", item.JobId);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(item.TempFilePath) && File.Exists(item.TempFilePath))
            {
                try { File.Delete(item.TempFilePath); } catch { /* ignore lock */ }
            }
        }
    }
}
