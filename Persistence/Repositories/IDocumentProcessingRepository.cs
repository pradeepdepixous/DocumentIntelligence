using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence.Persistence.Repositories;

/// <summary>
/// Repository abstraction separating database / artifact storage from pipeline orchestration.
/// Supports state transitions, audit logging, and job resumption.
/// </summary>
public interface IDocumentProcessingRepository
{
    Task SaveDocumentAsync(DocumentEntity document, CancellationToken cancellationToken = default);
    Task<DocumentEntity?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    Task SaveJobAsync(ExtractionJobEntity job, CancellationToken cancellationToken = default);
    Task<ExtractionJobEntity?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task UpdateJobStatusAsync(string jobId, string status, string? errorMessage = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a job failed with a retryable error and has been re-queued for a later
    /// attempt: sets Status to "RetryWait", stamps the attempt count and the earliest time the
    /// retry should run, and keeps the error for diagnostics.
    /// </summary>
    Task MarkJobRetryScheduledAsync(
        string jobId,
        int attemptCount,
        DateTime nextAttemptAt,
        string lastError,
        CancellationToken cancellationToken = default);

    Task SaveBatchManifestAsync(BatchManifest manifest, CancellationToken cancellationToken = default);
    Task<BatchManifest?> GetBatchManifestAsync(string jobId, CancellationToken cancellationToken = default);
    Task UpdateBatchStatusAsync(
        string jobId, 
        string batchId, 
        BatchStatus status, 
        int itemCount = 0, 
        long durationMs = 0, 
        string? errorMessage = null, 
        CancellationToken cancellationToken = default);

    Task SaveStitchedRecordAsync(
        string jobId, 
        string documentId, 
        DocumentEnvelope envelope, 
        ReconciliationLedger ledger, 
        CancellationToken cancellationToken = default);

    Task<DocumentEnvelope?> GetStitchedEnvelopeAsync(string jobId, CancellationToken cancellationToken = default);
    Task<ReconciliationLedger?> GetReconciliationLedgerAsync(string jobId, CancellationToken cancellationToken = default);

    Task SaveReviewTasksAsync(
        string jobId, 
        IEnumerable<ReviewTask> tasks, 
        CancellationToken cancellationToken = default);

    Task<List<ReviewTask>?> GetReviewTasksAsync(string jobId, CancellationToken cancellationToken = default);

    Task SaveProcessingResultAsync(string jobId, PixousDocIntelligence.Application.Models.DocumentProcessingResult result, CancellationToken cancellationToken = default);
    Task<PixousDocIntelligence.Application.Models.DocumentProcessingResult?> GetProcessingResultAsync(string jobId, CancellationToken cancellationToken = default);

    string GetJobDirectory(string jobId);
}
