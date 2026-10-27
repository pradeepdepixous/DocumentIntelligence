using System.Diagnostics;
using System.Text.Json;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Export;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Persistence.Repositories;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Pipeline.Extraction;
using PixousDocIntelligence.Pipeline.Ingestion;
using PixousDocIntelligence.Pipeline.Stitching;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence.Application.Orchestration;

/// <summary>
/// Orchestrates the end-to-end 7-phase document intelligence lifecycle:
/// Ingestion -&gt; Planning -&gt; Batching -&gt; Extraction -&gt; Stitching -&gt; Verification -&gt; Persistence &amp; Export.
/// Enforces job state transitions, resumable processing, and auditable evidence provenance.
/// </summary>
public class DocumentProcessingOrchestrator : IDocumentProcessingOrchestrator
{
    private readonly ProcessingOptions _options;
    private readonly IDocumentProcessingRepository _repository;
    private readonly IBatchScheduler _batchScheduler;
    private readonly IStitchingEngine _stitchingEngine;
    private readonly IEvidenceVerifier _evidenceVerifier;
    private readonly Services.LLM.ILLMClientFactory _llmClientFactory;

    public DocumentProcessingOrchestrator(
        ProcessingOptions options,
        IDocumentProcessingRepository? repository = null,
        IBatchScheduler? batchScheduler = null,
        IStitchingEngine? stitchingEngine = null,
        IEvidenceVerifier? evidenceVerifier = null,
        Services.LLM.ILLMClientFactory? llmClientFactory = null)
    {
        _options = options;
        _repository = repository ?? new InMemoryDocumentProcessingRepository();
        _batchScheduler = batchScheduler ?? new BatchScheduler();
        _stitchingEngine = stitchingEngine ?? new ConfidenceAwareStitcher();
        _evidenceVerifier = evidenceVerifier ?? new EvidenceVerifier();
        _llmClientFactory = llmClientFactory ?? new Services.LLM.LLMClientFactory(Microsoft.Extensions.Options.Options.Create(options));
    }

    public async Task<DocumentProcessingResult> ProcessDocumentAsync(
        DocumentProcessingRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentPath) || !File.Exists(request.DocumentPath))
        {
            throw new FileNotFoundException($"Document file not found: {request.DocumentPath}");
        }

        var totalStopwatch = Stopwatch.StartNew();
        string docFileName = Path.GetFileNameWithoutExtension(request.DocumentPath);

        // --- PHASE 1: DOCUMENT INGESTION & JOB TRACKING ---
        progress?.Report("[PHASE 1] Ingesting document, computing SHA-256 fingerprint and page count...");
        var ingestionService = new DocumentIngestionService();
        var job = await ingestionService.IngestDocumentAsync(request.DocumentPath, request.TenantId, cancellationToken);

        var docEntity = new DocumentEntity
        {
            DocumentId = job.DocumentId,
            TenantId = job.TenantId,
            FileName = Path.GetFileName(request.DocumentPath),
            FilePath = Path.GetFullPath(request.DocumentPath),
            FileSizeBytes = job.FileSizeBytes,
            Sha256Checksum = job.Sha256Checksum,
            PageCount = job.PageCount,
            Status = "Ingested",
            CreatedAt = job.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
        await _repository.SaveDocumentAsync(docEntity, cancellationToken);

        var jobEntity = new ExtractionJobEntity
        {
            JobId = job.JobId,
            DocumentId = job.DocumentId,
            TenantId = job.TenantId,
            Status = "Processing",
            UserInstruction = request.UserInstructions,
            SchemaVersion = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _repository.SaveJobAsync(jobEntity, cancellationToken);

        try
        {
            // Resolve LLM client dynamically based on per-request options, tier, or configuration
            var llmClient = _llmClientFactory.GetClient(request.Provider, request.ApiKey, request.Model, request.Tier);

            // --- PHASE 2: INSTRUCTION PLANNER & DYNAMIC SCHEMA ---
            progress?.Report($"[PHASE 2] Synthesizing extraction plan and dynamic schema with {llmClient.ProviderName} ({llmClient.ModelName})...");
            var planner = new InstructionPlanner(llmClient);
            string instructions = request.UserInstructions 
                ?? "Extract all critical business entities, dates, parties, contact details, services, and tables.";
            var plan = await planner.GeneratePlanAsync(instructions, documentTypeHint: null, cancellationToken: cancellationToken);

            jobEntity.ExtractionPlanJson = JsonSerializer.Serialize(plan);
            jobEntity.SchemaVersion = plan.SchemaVersion;
            await _repository.SaveJobAsync(jobEntity, cancellationToken);

            // --- PHASE 3: DOCUMENT MAPPER & BATCH SCHEDULER ---
            progress?.Report("[PHASE 3] Computing overlap batch manifest...");
            int batchSize = (job.PageCount <= _options.Batching.SmallDocThreshold) 
                ? _options.Batching.SmallDocBatchSize 
                : _options.Batching.DefaultBatchSize;
            int overlap = (job.PageCount <= _options.Batching.SmallDocThreshold)
                ? _options.Batching.SmallDocOverlapPages
                : _options.Batching.DefaultOverlapPages;

            var manifest = _batchScheduler.CreateManifest(
                job.JobId, 
                job.DocumentId, 
                job.PageCount, 
                batchSize, 
                overlap, 
                job.Sha256Checksum);
            await _repository.SaveBatchManifestAsync(manifest, cancellationToken);

            // --- PHASE 4: BATCH EXTRACTION (GEMINI OR OPENAI) ---
            progress?.Report($"[PHASE 4] Extracting {manifest.Batches.Count} batches via {llmClient.ProviderName} ({llmClient.ModelName})...");
            Pipeline.Extraction.IBatchExtractor batchExtractor = llmClient.ProviderName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? new Pipeline.Extraction.OpenAiBatchExtractor(llmClient)
                : new Pipeline.Extraction.GeminiBatchExtractor(request.ApiKey ?? _options.Gemini.ApiKey, llmClient.ModelName);

            var batchEnvelopes = new List<DocumentEnvelope>();

            foreach (var batch in manifest.Batches)
            {
                var batchWatch = Stopwatch.StartNew();
                progress?.Report($"  ↳ Extracting {batch.BatchId} (Pages {batch.StartPage}..{batch.EndPage})...");

                try
                {
                    byte[] batchBytes = await _batchScheduler.ExtractBatchBytesAsync(request.DocumentPath, batch, cancellationToken);
                    var envelope = await batchExtractor.ExtractBatchAsync(batchBytes, batch, plan, cancellationToken);
                    batchEnvelopes.Add(envelope);
                    batchWatch.Stop();

                    // Mutate this batch descriptor directly (not just the repository's persisted
                    // copy) so the manifest returned in the response reflects what actually happened.
                    batch.Status = BatchStatus.Completed;
                    batch.ExtractedItemCount = envelope.Items.Count;
                    batch.ProcessingDurationMs = batchWatch.ElapsedMilliseconds;
                    batch.ExecutionTimestamp = DateTime.UtcNow;

                    await _repository.UpdateBatchStatusAsync(
                        job.JobId,
                        batch.BatchId,
                        BatchStatus.Completed,
                        envelope.Items.Count,
                        batchWatch.ElapsedMilliseconds,
                        null,
                        cancellationToken);
                }
                catch (Exception batchEx)
                {
                    batchWatch.Stop();

                    batch.Status = BatchStatus.Failed;
                    batch.ProcessingDurationMs = batchWatch.ElapsedMilliseconds;
                    batch.ErrorMessage = batchEx.Message;
                    batch.ExecutionTimestamp = DateTime.UtcNow;

                    await _repository.UpdateBatchStatusAsync(
                        job.JobId,
                        batch.BatchId,
                        BatchStatus.Failed,
                        0,
                        batchWatch.ElapsedMilliseconds,
                        batchEx.Message,
                        cancellationToken);
                    throw;
                }
            }

            // --- PHASE 5: CONFIDENCE-AWARE STITCHING & CONFLICT LEDGER ---
            progress?.Report("[PHASE 5] Executing confidence-aware stitching & compiling conflict ledger...");
            var stitchingResult = _stitchingEngine.StitchBatchesDetailed(batchEnvelopes, plan);
            var stitchedEnvelope = stitchingResult.Envelope;
            var reconciliationLedger = stitchingResult.Ledger;

            // --- PHASE 6: EVIDENCE VERIFICATION & QUALITY CONTROL ---
            progress?.Report("[PHASE 6] Running deterministic evidence verification & quality calibration...");
            var qualityPolicy = new QualityPolicy
            {
                ReviewThreshold = _options.Verification.ReviewThreshold,
                UnverifiedPenalty = _options.Verification.UnverifiedPenalty
            };
            var verificationReport = await _evidenceVerifier.VerifyEvidenceAndCalibrateAsync(
                request.DocumentPath, 
                stitchedEnvelope, 
                qualityPolicy, 
                cancellationToken);

            // Under the markdown-first extraction design, Items/Tables are EXPECTED to be empty —
            // that's no longer a failure signal. The real completeness signals now are: did every
            // page get a markdown transcript, and did we get a document summary at all. Missing
            // either almost always means a malformed/incomplete model response for this run.
            var transcribedPages = stitchedEnvelope.Pages.Select(p => p.PageNumber).ToHashSet();
            var missingPages = Enumerable.Range(1, manifest.TotalPages).Where(p => !transcribedPages.Contains(p)).ToList();

            if (missingPages.Count > 0)
            {
                verificationReport.ReviewTasks.Add(new ReviewTask
                {
                    JobId = job.JobId,
                    FieldKey = "(none)",
                    PageNumber = missingPages[0],
                    Reason = "incomplete_transcription",
                    ReportedConfidence = 0,
                    SourceQuote = string.Empty,
                    ReviewerNotes = $"No markdown transcript was returned for page(s) {string.Join(", ", missingPages)} of {manifest.TotalPages}. " +
                                     "This usually means the extraction model's response for this run was malformed or incomplete — " +
                                     "retrying the same document is the first thing to try."
                });
            }

            if (string.IsNullOrWhiteSpace(stitchedEnvelope.DocumentSummary))
            {
                verificationReport.ReviewTasks.Add(new ReviewTask
                {
                    JobId = job.JobId,
                    FieldKey = "(none)",
                    PageNumber = 1,
                    Reason = "incomplete_transcription",
                    ReportedConfidence = 0,
                    SourceQuote = string.Empty,
                    ReviewerNotes = "No document summary was generated for this run. Retrying the same document is the first thing to try."
                });
            }

            // Persist stitched records and review tasks to repository
            await _repository.SaveStitchedRecordAsync(job.JobId, job.DocumentId, stitchedEnvelope, reconciliationLedger, cancellationToken);
            if (verificationReport.ReviewTasks.Count > 0)
            {
                await _repository.SaveReviewTasksAsync(job.JobId, verificationReport.ReviewTasks, cancellationToken);
            }

            // --- PHASE 7: MULTI-FORMAT EXPORT & JOB ISOLATION (IN-MEMORY FOR API TESTING) ---
            progress?.Report("[PHASE 7] Finalizing verified in-memory execution results...");

            // Finalize Job Status
            string finalStatus = (stitchedEnvelope.UnresolvedConflicts.Count > 0 || verificationReport.ReviewTasks.Count > 0)
                ? "NeedsReview"
                : "Succeeded";

            await _repository.UpdateJobStatusAsync(job.JobId, finalStatus, null, cancellationToken);
            totalStopwatch.Stop();

            return new DocumentProcessingResult
            {
                JobId = job.JobId,
                DocumentId = job.DocumentId,
                Status = finalStatus,
                Plan = plan,
                Manifest = manifest,
                StitchedEnvelope = stitchedEnvelope,
                ReconciliationLedger = reconciliationLedger,
                VerificationReport = verificationReport,
                OutputDirectory = "[in-memory]",
                EnvelopeJsonPath = "[in-memory]",
                ExtractedCsvPath = "[in-memory]",
                ReviewQueueCsvPath = verificationReport.ReviewTasks.Count > 0 ? "[in-memory]" : null,
                ReconciliationLedgerPath = "[in-memory]",
                ExecutionDurationMs = totalStopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            await _repository.UpdateJobStatusAsync(job.JobId, "Failed", ex.Message, cancellationToken);
            throw;
        }
    }
}
