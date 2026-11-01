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

        if (!string.IsNullOrWhiteSpace(request.JobId))
        {
            job.JobId = request.JobId;
        }
        if (!string.IsNullOrWhiteSpace(request.DocumentId))
        {
            job.DocumentId = request.DocumentId;
        }

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
            ExtractionPlan plan;
            bool isMarkdownMode = string.Equals(request.Mode, "markdown", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(request.Mode, "full_content", StringComparison.OrdinalIgnoreCase);

            if (isMarkdownMode)
            {
                progress?.Report("[PHASE 2] Markdown extraction requested: bypassing LLM schema planner for high-throughput transcription...");
                plan = new ExtractionPlan
                {
                    TargetDocumentType = "Document",
                    ExtractionMode = "markdown",
                    UserInstruction = request.UserInstructions ?? "Transcribe all pages into structured markdown, preserving tables.",
                    PreserveTables = true,
                    SchemaVersion = 2
                };
            }
            else if (!string.IsNullOrWhiteSpace(request.Schema))
            {
                progress?.Report("[PHASE 2] Loading manual JSON schema and bypassing LLM planner...");
                plan = SchemaParser.Parse(request.Schema, request.UserInstructions);
            }
            else
            {
                progress?.Report($"[PHASE 2] Synthesizing extraction plan and dynamic schema with {llmClient.ProviderName} ({llmClient.ModelName})...");
                var planner = new InstructionPlanner(llmClient);
                string instructions = request.UserInstructions 
                    ?? "Extract all critical business entities, dates, parties, contact details, services, and tables.";
                plan = await planner.GeneratePlanAsync(instructions, documentTypeHint: null, cancellationToken: cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(request.Mode))
            {
                plan.ExtractionMode = request.Mode;
            }

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

            // --- PHASE 4: BATCH EXTRACTION (GEMINI OR OPENAI VIA STRATEGY) ---
            var strategy = Pipeline.Extraction.ExtractionStrategyResolver.Resolve(plan, request.Mode);
            progress?.Report($"[PHASE 4] Extracting {manifest.Batches.Count} batches via {llmClient.ProviderName} ({llmClient.ModelName}) using [{strategy.ModeName}] strategy...");
            Pipeline.Extraction.IBatchExtractor batchExtractor = llmClient.ProviderName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? new Pipeline.Extraction.OpenAiBatchExtractor(llmClient, strategy)
                : new Pipeline.Extraction.GeminiBatchExtractor(request.ApiKey ?? _options.Gemini.ApiKey, llmClient.ModelName, strategy: strategy);

            var batchEnvelopes = new List<DocumentEnvelope>();
            var recoveryBudget = new RecoveryBudget { RemainingCalls = _options.Batching.MaxRecoveryCallsPerDocument };

            using var batchSource = _batchScheduler.OpenBatchSource(request.DocumentPath);

            foreach (var batch in manifest.Batches)
            {
                var batchWatch = Stopwatch.StartNew();
                progress?.Report($"  ↳ Extracting {batch.BatchId} (Pages {batch.StartPage}..{batch.EndPage})...");

                try
                {
                    var envelope = await ExtractBatchWithRecoveryAsync(
                        batchSource, batch, batchExtractor, plan, isMarkdownMode, recoveryBudget, progress, cancellationToken);
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

            // In markdown mode without structured items, verify transcript completeness
            if (stitchedEnvelope.Items.Count == 0)
            {
                if (stitchedEnvelope.Pages.Count == 0)
                {
                    verificationReport.ReviewTasks.Add(new ReviewTask
                    {
                        JobId = job.JobId,
                        FieldKey = "(none)",
                        PageNumber = 1,
                        Reason = "incomplete_transcription",
                        ReportedConfidence = 0,
                        SourceQuote = string.Empty,
                        ReviewerNotes = "No transcript pages were returned for this document at all. " +
                                         "This usually means the extraction model's response for this run was malformed or empty — " +
                                         "retrying the same document is the first thing to try."
                    });
                }
                else
                {
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
                }
            }

            // Flag any page where the model marked its own transcription as uncertain, so a human
            // can check the source image for that region instead of trusting the text silently.
            if (isMarkdownMode)
            {
                var unclearPages = stitchedEnvelope.Pages
                    .Where(p => p.Markdown.Contains("[unclear]", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.PageNumber)
                    .ToList();

                if (unclearPages.Count > 0)
                {
                    verificationReport.ReviewTasks.Add(new ReviewTask
                    {
                        JobId = job.JobId,
                        FieldKey = "(none)",
                        PageNumber = unclearPages[0],
                        Reason = "unclear_content",
                        ReportedConfidence = 0,
                        SourceQuote = string.Empty,
                        ReviewerNotes = $"Page(s) {string.Join(", ", unclearPages)} contain one or more [unclear] markers where the model " +
                                         "could not confidently read the source content. Review the original page image for these sections."
                    });
                }
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

            var result = new DocumentProcessingResult
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

            await _repository.SaveProcessingResultAsync(job.JobId, result, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await _repository.UpdateJobStatusAsync(job.JobId, "Failed", ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Extracts a batch and, in markdown mode, recovers from an incomplete transcript (missing or
    /// blank pages) by retrying the same page range, then by halving it into smaller sub-batches
    /// (down to a single page) until each slice comes back complete or can no longer be split.
    /// Non-markdown modes and transient transport failures are left to the caller/extractor as before.
    /// </summary>
    private async Task<DocumentEnvelope> ExtractBatchWithRecoveryAsync(
        Pipeline.Batching.IPdfBatchSource batchSource,
        BatchDescriptor batch,
        Pipeline.Extraction.IBatchExtractor batchExtractor,
        ExtractionPlan plan,
        bool isMarkdownMode,
        RecoveryBudget budget,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        const int maxSameSizeRetries = 1;

        byte[] batchBytes = await batchSource.ExtractBatchBytesAsync(batch, cancellationToken);
        var envelope = await batchExtractor.ExtractBatchAsync(batchBytes, batch, plan, cancellationToken);

        if (!isMarkdownMode || IsTranscriptionComplete(envelope, batch))
        {
            return envelope;
        }

        for (int attempt = 1; attempt <= maxSameSizeRetries; attempt++)
        {
            if (budget.RemainingCalls <= 0)
            {
                progress?.Report($"  ↳ {batch.BatchId} (Pages {batch.StartPage}..{batch.EndPage}) is incomplete, but the document-wide " +
                                  "recovery budget is exhausted — accepting it as-is and flagging it for review.");
                return envelope;
            }

            budget.RemainingCalls--;
            progress?.Report($"  ↳ {batch.BatchId} (Pages {batch.StartPage}..{batch.EndPage}) came back incomplete — " +
                              $"retrying (attempt {attempt}, {budget.RemainingCalls} recovery calls left)...");
            batch.RetryCount++;
            envelope = await batchExtractor.ExtractBatchAsync(batchBytes, batch, plan, cancellationToken);

            if (IsTranscriptionComplete(envelope, batch))
            {
                return envelope;
            }
        }

        if (batch.PageCount <= 1)
        {
            // Can't split a single page any further — hand back the best result we have;
            // the caller's completeness checks will flag the remaining gaps for human review.
            return envelope;
        }

        if (budget.RemainingCalls < 2)
        {
            // Splitting always costs two mandatory sub-batch extractions; don't start one we
            // can't afford to finish.
            progress?.Report($"  ↳ {batch.BatchId} still incomplete, but the document-wide recovery budget is exhausted — " +
                              "accepting the best result instead of splitting further.");
            return envelope;
        }

        progress?.Report($"  ↳ {batch.BatchId} still incomplete after retries — splitting Pages {batch.StartPage}..{batch.EndPage} " +
                          $"into smaller slices ({budget.RemainingCalls} recovery calls left)...");

        int midpoint = batch.StartPage + (batch.PageCount / 2) - 1;
        var firstHalf = new BatchDescriptor
        {
            BatchId = $"{batch.BatchId}-A",
            JobId = batch.JobId,
            DocumentId = batch.DocumentId,
            SourceDocumentChecksum = batch.SourceDocumentChecksum,
            StartPage = batch.StartPage,
            EndPage = midpoint,
            Status = BatchStatus.Pending
        };
        var secondHalf = new BatchDescriptor
        {
            BatchId = $"{batch.BatchId}-B",
            JobId = batch.JobId,
            DocumentId = batch.DocumentId,
            SourceDocumentChecksum = batch.SourceDocumentChecksum,
            StartPage = midpoint + 1,
            EndPage = batch.EndPage,
            Status = BatchStatus.Pending
        };

        // Charge the two mandatory first-attempt extractions the split is about to make; any
        // further retries/splits within each half are charged separately as they happen.
        budget.RemainingCalls -= 2;

        var firstEnvelope = await ExtractBatchWithRecoveryAsync(batchSource, firstHalf, batchExtractor, plan, isMarkdownMode, budget, progress, cancellationToken);
        var secondEnvelope = await ExtractBatchWithRecoveryAsync(batchSource, secondHalf, batchExtractor, plan, isMarkdownMode, budget, progress, cancellationToken);

        batch.RetryCount += firstHalf.RetryCount + secondHalf.RetryCount;

        return MergeSplitEnvelopes(batch, firstEnvelope, secondEnvelope);
    }

    /// <summary>
    /// Shared, mutable call budget passed by reference through one document's recovery
    /// recursion tree, so every batch's retries/splits draw from the same document-wide cap.
    /// </summary>
    private sealed class RecoveryBudget
    {
        public int RemainingCalls;
    }

    private static bool IsTranscriptionComplete(DocumentEnvelope envelope, BatchDescriptor batch)
    {
        var nonEmptyPages = envelope.Pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Markdown))
            .Select(p => p.PageNumber)
            .ToHashSet();

        for (int pageNumber = batch.StartPage; pageNumber <= batch.EndPage; pageNumber++)
        {
            if (!nonEmptyPages.Contains(pageNumber))
            {
                return false;
            }
        }

        return true;
    }

    private static DocumentEnvelope MergeSplitEnvelopes(BatchDescriptor originalBatch, DocumentEnvelope first, DocumentEnvelope second)
    {
        var merged = new DocumentEnvelope
        {
            DocumentId = originalBatch.DocumentId,
            JobId = originalBatch.JobId,
            BatchId = originalBatch.BatchId,
            SchemaVersion = Math.Max(first.SchemaVersion, second.SchemaVersion),
            CreatedAt = DateTime.UtcNow
        };

        merged.Pages.AddRange(first.Pages);
        merged.Pages.AddRange(second.Pages);
        merged.Items.AddRange(first.Items);
        merged.Items.AddRange(second.Items);
        merged.Tables.AddRange(first.Tables);
        merged.Tables.AddRange(second.Tables);

        return merged;
    }
}
