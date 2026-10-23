using System.Collections.Concurrent;
using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence.Persistence.Repositories;

/// <summary>
/// Ephemeral, in-memory implementation of IDocumentProcessingRepository.
/// Keeps job execution state, stitched envelopes, and reconciliation ledgers in memory for live API testing
/// without writing job response files, JSON envelopes, or CSV exports to the local file system.
/// </summary>
public class InMemoryDocumentProcessingRepository : IDocumentProcessingRepository
{
    private readonly ConcurrentDictionary<string, DocumentEntity> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ExtractionJobEntity> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BatchManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DocumentEnvelope> _envelopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ReconciliationLedger> _ledgers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ReviewTask>> _reviewTasks = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveDocumentAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        _documents[document.DocumentId] = document;
        return Task.CompletedTask;
    }

    public Task<DocumentEntity?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        _documents.TryGetValue(documentId, out var doc);
        return Task.FromResult(doc);
    }

    public Task SaveJobAsync(ExtractionJobEntity job, CancellationToken cancellationToken = default)
    {
        _jobs[job.JobId] = job;
        return Task.CompletedTask;
    }

    public Task<ExtractionJobEntity?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task UpdateJobStatusAsync(string jobId, string status, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = status;
            job.ErrorMessage = errorMessage;
            job.UpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SaveBatchManifestAsync(BatchManifest manifest, CancellationToken cancellationToken = default)
    {
        _manifests[manifest.JobId] = manifest;
        return Task.CompletedTask;
    }

    public Task<BatchManifest?> GetBatchManifestAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _manifests.TryGetValue(jobId, out var manifest);
        return Task.FromResult(manifest);
    }

    public Task UpdateBatchStatusAsync(
        string jobId, 
        string batchId, 
        BatchStatus status, 
        int itemCount = 0, 
        long durationMs = 0, 
        string? errorMessage = null, 
        CancellationToken cancellationToken = default)
    {
        if (_manifests.TryGetValue(jobId, out var manifest))
        {
            var batch = manifest.Batches.FirstOrDefault(b => b.BatchId.Equals(batchId, StringComparison.OrdinalIgnoreCase));
            if (batch != null)
            {
                batch.Status = status;
                batch.ExtractedItemCount = itemCount;
                batch.ProcessingDurationMs = durationMs;
                batch.ErrorMessage = errorMessage;
                batch.ExecutionTimestamp = DateTime.UtcNow;
            }
        }
        return Task.CompletedTask;
    }

    public Task SaveStitchedRecordAsync(
        string jobId, 
        string documentId, 
        DocumentEnvelope envelope, 
        ReconciliationLedger ledger, 
        CancellationToken cancellationToken = default)
    {
        _envelopes[jobId] = envelope;
        _ledgers[jobId] = ledger;
        return Task.CompletedTask;
    }

    public Task<DocumentEnvelope?> GetStitchedEnvelopeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _envelopes.TryGetValue(jobId, out var envelope);
        return Task.FromResult(envelope);
    }

    public Task<ReconciliationLedger?> GetReconciliationLedgerAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _ledgers.TryGetValue(jobId, out var ledger);
        return Task.FromResult(ledger);
    }

    public Task SaveReviewTasksAsync(
        string jobId, 
        IEnumerable<ReviewTask> tasks, 
        CancellationToken cancellationToken = default)
    {
        _reviewTasks[jobId] = tasks.ToList();
        return Task.CompletedTask;
    }

    public Task<List<ReviewTask>?> GetReviewTasksAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _reviewTasks.TryGetValue(jobId, out var tasks);
        return Task.FromResult(tasks);
    }

    public string GetJobDirectory(string jobId) => string.Empty;
}
