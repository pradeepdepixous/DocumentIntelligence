using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence.Persistence.Repositories;

/// <summary>
/// PostgreSQL + Entity Framework Core implementation of IDocumentProcessingRepository.
/// Stores relational metadata, batches, JSONB envelopes, reconciliation ledgers, and review tasks.
/// </summary>
public class PostgresDocumentProcessingRepository : IDocumentProcessingRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgresDocumentProcessingRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public PostgresDocumentProcessingRepository(
        IServiceScopeFactory scopeFactory,
        ILogger<PostgresDocumentProcessingRepository> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task SaveDocumentAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var existing = await db.Documents.FindAsync(new object[] { document.DocumentId }, cancellationToken);
        if (existing == null)
        {
            await db.Documents.AddAsync(document, cancellationToken);
        }
        else
        {
            existing.FileName = document.FileName;
            existing.FilePath = document.FilePath;
            existing.FileSizeBytes = document.FileSizeBytes;
            existing.Sha256Checksum = document.Sha256Checksum;
            existing.PageCount = document.PageCount;
            existing.Status = document.Status;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DocumentEntity?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        return await db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
    }

    public async Task SaveJobAsync(ExtractionJobEntity job, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var existing = await db.ExtractionJobs.FindAsync(new object[] { job.JobId }, cancellationToken);
        if (existing == null)
        {
            await db.ExtractionJobs.AddAsync(job, cancellationToken);
        }
        else
        {
            existing.Status = job.Status;
            existing.UserInstruction = job.UserInstruction;
            existing.SchemaVersion = job.SchemaVersion;
            existing.ExtractionPlanJson = job.ExtractionPlanJson;
            existing.ErrorMessage = job.ErrorMessage;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExtractionJobEntity?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        return await db.ExtractionJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
    }

    public async Task UpdateJobStatusAsync(string jobId, string status, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var job = await db.ExtractionJobs.FindAsync(new object[] { jobId }, cancellationToken);
        if (job != null)
        {
            job.Status = status;
            job.ErrorMessage = errorMessage;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SaveBatchManifestAsync(BatchManifest manifest, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        string manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);

        var existingManifest = await db.BatchManifests.FindAsync(new object[] { manifest.JobId }, cancellationToken);
        if (existingManifest == null)
        {
            await db.BatchManifests.AddAsync(new BatchManifestEntity
            {
                JobId = manifest.JobId,
                DocumentId = manifest.DocumentId,
                TotalPages = manifest.TotalPages,
                BatchSize = manifest.BatchSize,
                OverlapSize = manifest.OverlapSize,
                ManifestPayloadJson = manifestJson,
                CreatedAt = manifest.CreatedAt
            }, cancellationToken);
        }
        else
        {
            existingManifest.DocumentId = manifest.DocumentId;
            existingManifest.TotalPages = manifest.TotalPages;
            existingManifest.BatchSize = manifest.BatchSize;
            existingManifest.OverlapSize = manifest.OverlapSize;
            existingManifest.ManifestPayloadJson = manifestJson;
        }

        // Upsert individual batch descriptor entities
        foreach (var b in manifest.Batches)
        {
            var existingBatch = await db.ExtractionBatches.FindAsync(new object[] { manifest.JobId, b.BatchId }, cancellationToken);
            if (existingBatch == null)
            {
                await db.ExtractionBatches.AddAsync(new ExtractionBatchEntity
                {
                    JobId = manifest.JobId,
                    BatchId = b.BatchId,
                    StartPage = b.StartPage,
                    EndPage = b.EndPage,
                    OverlapStartPage = b.OverlapStartPage,
                    OverlapEndPage = b.OverlapEndPage,
                    Status = b.Status.ToString(),
                    RetryCount = b.RetryCount,
                    ExtractedItemCount = b.ExtractedItemCount,
                    ProcessingDurationMs = b.ProcessingDurationMs,
                    ErrorMessage = b.ErrorMessage,
                    UpdatedAt = DateTime.UtcNow
                }, cancellationToken);
            }
            else
            {
                existingBatch.Status = b.Status.ToString();
                existingBatch.RetryCount = b.RetryCount;
                existingBatch.ExtractedItemCount = b.ExtractedItemCount;
                existingBatch.ProcessingDurationMs = b.ProcessingDurationMs;
                existingBatch.ErrorMessage = b.ErrorMessage;
                existingBatch.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<BatchManifest?> GetBatchManifestAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var entity = await db.BatchManifests
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.JobId == jobId, cancellationToken);

        if (entity == null || string.IsNullOrWhiteSpace(entity.ManifestPayloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BatchManifest>(entity.ManifestPayloadJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize BatchManifest for job {JobId}", jobId);
            return null;
        }
    }

    public async Task UpdateBatchStatusAsync(
        string jobId,
        string batchId,
        BatchStatus status,
        int itemCount = 0,
        long durationMs = 0,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var batch = await db.ExtractionBatches.FindAsync(new object[] { jobId, batchId }, cancellationToken);
        if (batch != null)
        {
            batch.Status = status.ToString();
            batch.ExtractedItemCount = itemCount;
            batch.ProcessingDurationMs = durationMs;
            batch.ErrorMessage = errorMessage;
            batch.UpdatedAt = DateTime.UtcNow;
        }

        // Also update the nested JSON payload in BatchManifestEntity for synchronized retrieval
        var manifestEntity = await db.BatchManifests.FindAsync(new object[] { jobId }, cancellationToken);
        if (manifestEntity != null && !string.IsNullOrWhiteSpace(manifestEntity.ManifestPayloadJson))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<BatchManifest>(manifestEntity.ManifestPayloadJson, JsonOptions);
                var targetBatch = manifest?.Batches.FirstOrDefault(b => b.BatchId == batchId);
                if (targetBatch != null)
                {
                    targetBatch.Status = status;
                    targetBatch.ExtractedItemCount = itemCount;
                    targetBatch.ProcessingDurationMs = durationMs;
                    targetBatch.ErrorMessage = errorMessage;
                    targetBatch.ExecutionTimestamp = DateTime.UtcNow;
                    manifestEntity.ManifestPayloadJson = JsonSerializer.Serialize(manifest, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync in-payload batch descriptor for job {JobId}, batch {BatchId}", jobId, batchId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveStitchedRecordAsync(
        string jobId,
        string documentId,
        DocumentEnvelope envelope,
        ReconciliationLedger ledger,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        string envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
        string ledgerJson = JsonSerializer.Serialize(ledger, JsonOptions);

        // 1. Stitched Envelope Record
        var record = await db.StitchedRecords.FirstOrDefaultAsync(r => r.JobId == jobId, cancellationToken);
        if (record == null)
        {
            await db.StitchedRecords.AddAsync(new StitchedRecordEntity
            {
                RecordId = Guid.NewGuid().ToString("N"),
                JobId = jobId,
                DocumentId = documentId,
                ConsolidatedPayloadJson = envelopeJson,
                UnresolvedConflictsCount = ledger.ConflictCount,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        else
        {
            record.DocumentId = documentId;
            record.ConsolidatedPayloadJson = envelopeJson;
            record.UnresolvedConflictsCount = ledger.ConflictCount;
        }

        // 2. Reconciliation Ledger Record
        var existingLedger = await db.ReconciliationLedgers.FirstOrDefaultAsync(l => l.JobId == jobId, cancellationToken);
        if (existingLedger == null)
        {
            await db.ReconciliationLedgers.AddAsync(new ReconciliationLedgerEntity
            {
                JobId = jobId,
                DocumentId = documentId,
                LedgerPayloadJson = ledgerJson,
                ConflictCount = ledger.ConflictCount,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        else
        {
            existingLedger.DocumentId = documentId;
            existingLedger.LedgerPayloadJson = ledgerJson;
            existingLedger.ConflictCount = ledger.ConflictCount;
        }

        // SaveChangesAsync is atomic across both records in EF Core
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DocumentEnvelope?> GetStitchedEnvelopeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var record = await db.StitchedRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.JobId == jobId, cancellationToken);

        if (record == null || string.IsNullOrWhiteSpace(record.ConsolidatedPayloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DocumentEnvelope>(record.ConsolidatedPayloadJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize DocumentEnvelope for job {JobId}", jobId);
            return null;
        }
    }

    public async Task<ReconciliationLedger?> GetReconciliationLedgerAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var record = await db.ReconciliationLedgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.JobId == jobId, cancellationToken);

        if (record == null || string.IsNullOrWhiteSpace(record.LedgerPayloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReconciliationLedger>(record.LedgerPayloadJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize ReconciliationLedger for job {JobId}", jobId);
            return null;
        }
    }

    public async Task SaveReviewTasksAsync(
        string jobId,
        IEnumerable<ReviewTask> tasks,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        foreach (var task in tasks)
        {
            var existing = await db.ReviewTasks.FindAsync(new object[] { task.TaskId }, cancellationToken);
            if (existing == null)
            {
                await db.ReviewTasks.AddAsync(new ReviewTaskEntity
                {
                    TaskId = task.TaskId,
                    JobId = jobId,
                    FieldKey = task.FieldKey,
                    PageNumber = task.PageNumber,
                    Reason = task.Reason,
                    ReportedValueJson = task.ReportedValue != null ? JsonSerializer.Serialize(task.ReportedValue, JsonOptions) : "{}",
                    ReportedConfidence = (decimal)task.ReportedConfidence,
                    SourceQuote = task.SourceQuote,
                    Status = task.Status,
                    ReviewerNotes = task.ReviewerNotes,
                    ResolvedValueJson = task.ResolvedValue != null ? JsonSerializer.Serialize(task.ResolvedValue, JsonOptions) : null,
                    CreatedAt = DateTime.UtcNow,
                    ResolvedAt = task.Status != "open" ? DateTime.UtcNow : null
                }, cancellationToken);
            }
            else
            {
                existing.Status = task.Status;
                existing.ReviewerNotes = task.ReviewerNotes;
                existing.ResolvedValueJson = task.ResolvedValue != null ? JsonSerializer.Serialize(task.ResolvedValue, JsonOptions) : null;
                existing.ResolvedAt = task.Status != "open" ? DateTime.UtcNow : null;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ReviewTask>?> GetReviewTasksAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var entities = await db.ReviewTasks
            .AsNoTracking()
            .Where(t => t.JobId == jobId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0)
        {
            return new List<ReviewTask>();
        }

        var results = new List<ReviewTask>();
        foreach (var e in entities)
        {
            object? reportedVal = null;
            if (!string.IsNullOrWhiteSpace(e.ReportedValueJson) && e.ReportedValueJson != "{}")
            {
                try
                {
                    reportedVal = JsonSerializer.Deserialize<object>(e.ReportedValueJson, JsonOptions);
                }
                catch
                {
                    reportedVal = e.ReportedValueJson;
                }
            }

            object? resolvedVal = null;
            if (!string.IsNullOrWhiteSpace(e.ResolvedValueJson))
            {
                try
                {
                    resolvedVal = JsonSerializer.Deserialize<object>(e.ResolvedValueJson, JsonOptions);
                }
                catch
                {
                    resolvedVal = e.ResolvedValueJson;
                }
            }

            results.Add(new ReviewTask
            {
                TaskId = e.TaskId,
                JobId = e.JobId,
                FieldKey = e.FieldKey,
                PageNumber = e.PageNumber,
                Reason = e.Reason,
                ReportedValue = reportedVal,
                ReportedConfidence = (double)e.ReportedConfidence,
                SourceQuote = e.SourceQuote ?? string.Empty,
                Status = e.Status,
                ReviewerNotes = e.ReviewerNotes,
                ResolvedValue = resolvedVal
            });
        }

        return results;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PixousDocIntelligence.Application.Models.DocumentProcessingResult> ResultCache = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveProcessingResultAsync(string jobId, PixousDocIntelligence.Application.Models.DocumentProcessingResult result, CancellationToken cancellationToken = default)
    {
        ResultCache[jobId] = result;
        return Task.CompletedTask;
    }

    public async Task<PixousDocIntelligence.Application.Models.DocumentProcessingResult?> GetProcessingResultAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (ResultCache.TryGetValue(jobId, out var cached))
        {
            return cached;
        }

        var job = await GetJobAsync(jobId, cancellationToken);
        if (job == null) return null;

        var envelope = await GetStitchedEnvelopeAsync(jobId, cancellationToken);
        var manifest = await GetBatchManifestAsync(jobId, cancellationToken);
        var ledger = await GetReconciliationLedgerAsync(jobId, cancellationToken);
        var reviewTasks = await GetReviewTasksAsync(jobId, cancellationToken);

        return new PixousDocIntelligence.Application.Models.DocumentProcessingResult
        {
            JobId = job.JobId,
            DocumentId = job.DocumentId,
            Status = job.Status,
            Manifest = manifest ?? new BatchManifest(),
            StitchedEnvelope = envelope ?? new DocumentEnvelope(),
            ReconciliationLedger = ledger ?? new ReconciliationLedger(),
            VerificationReport = new VerificationReport
            {
                ReviewTasks = reviewTasks ?? new List<ReviewTask>()
            },
            OutputDirectory = "[in-memory]",
            EnvelopeJsonPath = "[in-memory]",
            ExtractedCsvPath = "[in-memory]",
            ReconciliationLedgerPath = "[in-memory]"
        };
    }

    public string GetJobDirectory(string jobId)
    {
        // Pure PostgreSQL storage does not rely on disk folders
        return string.Empty;
    }
}
