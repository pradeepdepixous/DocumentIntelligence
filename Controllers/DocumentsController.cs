using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Orchestration;
using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Persistence.Repositories;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Pipeline.Verification;
using PixousDocIntelligence.Security;
using PixousDocIntelligence.Services.Accounts;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Endpoints for ingesting, processing, and querying documents through the 7-phase intelligence pipeline.
/// Accepts either an external integration's API key or a signed-in portal session (see
/// <see cref="PortalAuthSchemes.Combined"/>) — the latter lets the Playground exercise these
/// endpoints directly against the user's own account credit, with no API key required.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentProcessingOrchestrator _orchestrator;
    private readonly IDocumentProcessingRepository _repository;
    private readonly Credentials.ICredentialRegistry _credentialRegistry;
    private readonly CustomerAccountService _accountService;
    private readonly PixousDocIntelligence.Application.Jobs.IDocumentJobQueue _jobQueue;

    public DocumentsController(
        IDocumentProcessingOrchestrator orchestrator,
        IDocumentProcessingRepository repository,
        Credentials.ICredentialRegistry credentialRegistry,
        CustomerAccountService accountService,
        PixousDocIntelligence.Application.Jobs.IDocumentJobQueue jobQueue)
    {
        _orchestrator = orchestrator;
        _repository = repository;
        _credentialRegistry = credentialRegistry;
        _accountService = accountService;
        _jobQueue = jobQueue;
    }

    /// <summary>
    /// Uploads and processes a document (PDF up to 100+ pages) through the 7-phase pipeline.
    /// Supports 'fast' (Gemini Flash-Lite, default) and 'premium' (OpenAI GPT-4o) engine tiers.
    /// </summary>
    /// <param name="upload">The multipart form payload containing the document file, tier, and optional extraction instructions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The consolidated execution result including verified field envelope, review tasks, and artifact references.</returns>
    [HttpPost("process")]
    [Consumes("multipart/form-data")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "process,admin")]
    [ProducesResponseType(typeof(DocumentProcessingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ProcessDocument(
        [FromForm] ProcessDocumentUploadRequest upload,
        CancellationToken cancellationToken = default)
    {
        if (upload.File == null || upload.File.Length == 0)
        {
            return BadRequest(new { error = "A non-empty document file must be uploaded." });
        }

        string extension = Path.GetExtension(upload.File.FileName).ToLowerInvariant();
        if (extension != ".pdf")
        {
            return BadRequest(new { error = $"Currently only PDF documents are supported for batch intelligence. Provided: '{extension}'" });
        }

        // Save uploaded stream to a secure temporary staging location
        string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        if (!Directory.Exists(uploadsDir))
        {
            Directory.CreateDirectory(uploadsDir);
        }

        string tempFilePath = Path.Combine(uploadsDir, $"{Guid.NewGuid():N}_{upload.File.FileName}");
        await using (var stream = new FileStream(tempFilePath, FileMode.Create))
        {
            await upload.File.CopyToAsync(stream, cancellationToken);
        }

        try
        {
            // 1. Derive identity and billing source from whichever scheme authenticated this request:
            //    - API key with no owning portal account (legacy/admin key): bill the key itself, as before.
            //    - API key owned by a portal account (self-serve key): bill the ACCOUNT's shared credit pool.
            //    - Portal session with no API key at all (Playground): bill that account directly.
            string? clientId = User.FindFirst("client_id")?.Value;
            string? sessionAccountId = User.FindFirst("account_id")?.Value;
            string? billedAccountId = sessionAccountId;

            if (clientId != null)
            {
                var credential = await _credentialRegistry.GetClientByIdAsync(clientId, cancellationToken);
                billedAccountId = credential?.OwnerAccountId;
            }

            string clientIdentifier = clientId ?? sessionAccountId ?? upload.TenantId ?? "default-tenant";

            // 2. Pre-execution quota validation
            int incomingPages = 1;
            try
            {
                using var pdfProbe = UglyToad.PdfPig.PdfDocument.Open(tempFilePath);
                incomingPages = pdfProbe.NumberOfPages;
            }
            catch { /* fallback to 1 page if probe fails */ }

            bool quotaAllowed;
            int remainingBefore;
            string quotaMsg;
            if (billedAccountId != null)
            {
                (quotaAllowed, remainingBefore, quotaMsg) = await _accountService.CheckCreditAsync(billedAccountId, incomingPages, cancellationToken);
            }
            else
            {
                (quotaAllowed, remainingBefore, quotaMsg) = await _credentialRegistry.CheckQuotaAsync(clientIdentifier, incomingPages, cancellationToken);
            }

            if (!quotaAllowed)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "MonthlyQuotaExceeded",
                    message = quotaMsg,
                    requested_pages = incomingPages,
                    remaining_pages = remainingBefore
                });
            }

            string? manualSchema = upload.Schema;
            if (string.IsNullOrWhiteSpace(manualSchema) && upload.SchemaFile != null && upload.SchemaFile.Length > 0)
            {
                using var reader = new StreamReader(upload.SchemaFile.OpenReadStream());
                manualSchema = await reader.ReadToEndAsync(cancellationToken);
            }
            if (string.IsNullOrWhiteSpace(manualSchema))
            {
                manualSchema = Request.Headers["X-Extraction-Schema"].FirstOrDefault();
            }

            bool isAsync = upload.Async == true || Request.Headers["Prefer"].ToString().Contains("respond-async", StringComparison.OrdinalIgnoreCase);

            if (isAsync)
            {
                string jobId = $"JOB-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
                string documentId = $"DOC-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

                var docEntity = new DocumentEntity
                {
                    DocumentId = documentId,
                    TenantId = clientIdentifier,
                    FileName = Path.GetFileName(upload.File.FileName),
                    FilePath = Path.GetFullPath(tempFilePath),
                    FileSizeBytes = upload.File.Length,
                    PageCount = incomingPages,
                    Status = "Queued",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _repository.SaveDocumentAsync(docEntity, cancellationToken);

                var jobEntity = new ExtractionJobEntity
                {
                    JobId = jobId,
                    DocumentId = documentId,
                    TenantId = clientIdentifier,
                    Status = "Queued",
                    UserInstruction = upload.Instructions,
                    SchemaVersion = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _repository.SaveJobAsync(jobEntity, cancellationToken);

                var workItem = new PixousDocIntelligence.Application.Jobs.DocumentJobWorkItem
                {
                    JobId = jobId,
                    DocumentId = documentId,
                    Request = new DocumentProcessingRequest
                    {
                        JobId = jobId,
                        DocumentId = documentId,
                        DocumentPath = tempFilePath,
                        UserInstructions = upload.Instructions,
                        TenantId = clientIdentifier,
                        Tier = upload.Tier ?? Request.Headers["X-LLM-Tier"].FirstOrDefault() ?? "fast",
                        Provider = upload.Provider ?? Request.Headers["X-LLM-Provider"].FirstOrDefault(),
                        Model = upload.Model ?? Request.Headers["X-LLM-Model"].FirstOrDefault(),
                        ApiKey = upload.ApiKey ?? Request.Headers["X-LLM-ApiKey"].FirstOrDefault(),
                        Mode = upload.Mode ?? Request.Headers["X-Extraction-Mode"].FirstOrDefault() ?? "schema",
                        Schema = manualSchema
                    },
                    ClientIdentifier = clientIdentifier,
                    BilledAccountId = billedAccountId,
                    ClientId = clientId,
                    IncomingPages = incomingPages,
                    RemainingBefore = remainingBefore,
                    TempFilePath = tempFilePath
                };

                await _jobQueue.EnqueueAsync(workItem, cancellationToken);

                Response.Headers["Location"] = $"/api/documents/jobs/{jobId}/result";
                return StatusCode(StatusCodes.Status202Accepted, new AsyncJobAcceptedResponse
                {
                    JobId = jobId,
                    DocumentId = documentId,
                    Status = "Queued",
                    Message = $"Document queued for asynchronous background processing ({incomingPages} pages). Check statusUrl or resultUrl.",
                    CreatedAt = DateTime.UtcNow,
                    StatusUrl = $"/api/documents/jobs/{jobId}",
                    ResultUrl = $"/api/documents/jobs/{jobId}/result",
                    EnvelopeUrl = $"/api/documents/jobs/{jobId}/envelope",
                    CsvUrl = $"/api/documents/jobs/{jobId}/csv",
                    MarkdownUrl = $"/api/documents/jobs/{jobId}/markdown"
                });
            }

            var request = new DocumentProcessingRequest
            {
                DocumentPath = tempFilePath,
                UserInstructions = upload.Instructions,
                TenantId = clientIdentifier,
                Tier = upload.Tier ?? Request.Headers["X-LLM-Tier"].FirstOrDefault() ?? "fast",
                Provider = upload.Provider ?? Request.Headers["X-LLM-Provider"].FirstOrDefault(),
                Model = upload.Model ?? Request.Headers["X-LLM-Model"].FirstOrDefault(),
                ApiKey = upload.ApiKey ?? Request.Headers["X-LLM-ApiKey"].FirstOrDefault(),
                Mode = upload.Mode ?? Request.Headers["X-Extraction-Mode"].FirstOrDefault() ?? "schema",
                Schema = manualSchema
            };

            var result = await _orchestrator.ProcessDocumentAsync(request, cancellationToken: cancellationToken);

            // 3. Record usage: the account's shared pool is the actual enforcement ledger whenever
            //    one applies; the key's own counters (when a key was used) are kept purely for its
            //    own "pages used" display in API Access/Overview, not for gating.
            int actualPages = result.Manifest.TotalPages > 0 ? result.Manifest.TotalPages : incomingPages;
            if (billedAccountId != null)
            {
                await _accountService.RecordCreditUsageAsync(billedAccountId, actualPages, cancellationToken);
                if (clientId != null)
                {
                    await _credentialRegistry.RecordUsageAsync(clientId, actualPages, "document_process", cancellationToken);
                }
            }
            else
            {
                await _credentialRegistry.RecordUsageAsync(clientIdentifier, actualPages, "document_process", cancellationToken);
            }

            // 4. Attach enterprise response headers
            int remainingAfter = Math.Max(0, remainingBefore - actualPages);
            Response.Headers["X-Pages-Processed"] = actualPages.ToString();
            Response.Headers["X-Quota-Remaining"] = remainingAfter.ToString();
            Response.Headers["X-Client-Id"] = clientIdentifier;

            return Ok(result);
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429"))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "Gemini API Quota Exceeded (HTTP 429)",
                message = "The Google Gemini free-tier daily quota has been reached on this API key. Please configure a paid API key or wait for the quota window to reset.",
                details = httpEx.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Document processing failed",
                message = ex.Message,
                details = ex.InnerException?.Message
            });
        }
        finally
        {
            // Clean up staging upload only for synchronous executions; async worker cleans up when done
            bool isAsyncRequest = upload.Async == true || Request.Headers["Prefer"].ToString().Contains("respond-async", StringComparison.OrdinalIgnoreCase);
            if (!isAsyncRequest && System.IO.File.Exists(tempFilePath))
            {
                try { System.IO.File.Delete(tempFilePath); } catch { /* Ignore file lock */ }
            }
        }
    }

    /// <summary>
    /// Explicit asynchronous processing endpoint for large documents (50 to 100+ pages).
    /// Instantly enqueues the document and returns 202 Accepted with polling URLs.
    /// </summary>
    [HttpPost("process-async")]
    [Consumes("multipart/form-data")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "process,admin")]
    [ProducesResponseType(typeof(AsyncJobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> ProcessDocumentAsync(
        [FromForm] ProcessDocumentUploadRequest upload,
        CancellationToken cancellationToken = default)
    {
        upload.Async = true;
        return ProcessDocument(upload, cancellationToken);
    }

    /// <summary>
    /// Retrieves the job execution status and metadata for a given Job ID.
    /// </summary>
    /// <param name="jobId">The unique Job ID (e.g. 'JOB-157ED562').</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(typeof(ExtractionJobEntity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobStatus(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _repository.GetJobAsync(jobId, cancellationToken);
        if (job == null)
        {
            return NotFound(new { error = $"Job '{jobId}' not found." });
        }
        return Ok(job);
    }

    /// <summary>
    /// Retrieves the completed DocumentProcessingResult for an asynchronous job, or 202 Accepted if still processing.
    /// </summary>
    /// <param name="jobId">The unique Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("jobs/{jobId}/result")]
    [ProducesResponseType(typeof(DocumentProcessingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetJobResult(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _repository.GetJobAsync(jobId, cancellationToken);
        if (job == null)
        {
            return NotFound(new { error = $"Job '{jobId}' not found." });
        }

        if (job.Status == "Queued" || job.Status == "Processing")
        {
            Response.Headers["Retry-After"] = "5";
            return StatusCode(StatusCodes.Status202Accepted, new
            {
                job_id = jobId,
                status = job.Status,
                message = "Document processing is currently in progress. Please poll again shortly.",
                created_at = job.CreatedAt,
                updated_at = job.UpdatedAt
            });
        }

        if (job.Status == "Failed")
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                job_id = jobId,
                status = job.Status,
                error = "ProcessingFailed",
                message = job.ErrorMessage ?? "Job processing encountered an unrecoverable failure."
            });
        }

        var result = await _repository.GetProcessingResultAsync(jobId, cancellationToken);
        if (result == null)
        {
            var envelope = await _repository.GetStitchedEnvelopeAsync(jobId, cancellationToken);
            var manifest = await _repository.GetBatchManifestAsync(jobId, cancellationToken);
            var ledger = await _repository.GetReconciliationLedgerAsync(jobId, cancellationToken);
            var reviewTasks = await _repository.GetReviewTasksAsync(jobId, cancellationToken);

            result = new DocumentProcessingResult
            {
                JobId = jobId,
                DocumentId = job.DocumentId,
                Status = job.Status,
                Manifest = manifest ?? new BatchManifest(),
                StitchedEnvelope = envelope ?? new DocumentEnvelope(),
                ReconciliationLedger = ledger ?? new ReconciliationLedger(),
                VerificationReport = new VerificationReport { ReviewTasks = reviewTasks ?? new List<ReviewTask>() }
            };
        }

        return Ok(result);
    }

    /// <summary>
    /// Retrieves the verified, canonical JSON DocumentEnvelope for a completed job.
    /// </summary>
    /// <param name="jobId">The unique Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("jobs/{jobId}/envelope")]
    [ProducesResponseType(typeof(DocumentEnvelope), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEnvelope(string jobId, CancellationToken cancellationToken = default)
    {
        var envelope = await _repository.GetStitchedEnvelopeAsync(jobId, cancellationToken);
        if (envelope == null)
        {
            return NotFound(new { error = $"Stitched envelope for Job '{jobId}' not found." });
        }
        return Ok(envelope);
    }

    /// <summary>
    /// Downloads the RFC 4180-compliant CSV export generated for a completed job.
    /// </summary>
    /// <param name="jobId">The unique Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("jobs/{jobId}/csv")]
    [Produces("text/csv")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadCsv(string jobId, CancellationToken cancellationToken = default)
    {
        var envelope = await _repository.GetStitchedEnvelopeAsync(jobId, cancellationToken);
        if (envelope == null)
        {
            return NotFound(new { error = $"Job '{jobId}' or its envelope not found in memory." });
        }

        string csvContent = Export.DocumentExportService.ExportToCsv(envelope);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
        return File(bytes, "text/csv", $"{jobId}_extracted.csv");
    }

    /// <summary>
    /// Downloads the full verbatim Markdown transcript reconstructed across all pages of the document.
    /// </summary>
    /// <param name="jobId">The unique Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("jobs/{jobId}/markdown")]
    [Produces("text/markdown")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadMarkdown(string jobId, CancellationToken cancellationToken = default)
    {
        var envelope = await _repository.GetStitchedEnvelopeAsync(jobId, cancellationToken);
        if (envelope == null || string.IsNullOrWhiteSpace(envelope.FullMarkdown))
        {
            return NotFound(new { error = $"Job '{jobId}' or its Markdown transcript not found." });
        }

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(envelope.FullMarkdown);
        return File(bytes, "text/markdown", $"{jobId}_transcript.md");
    }

    /// <summary>
    /// Retrieves the explainable Reconciliation Ledger recording all stitching decisions and conflict candidate observations.
    /// </summary>
    /// <param name="jobId">The unique Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("jobs/{jobId}/ledger")]
    [ProducesResponseType(typeof(ReconciliationLedger), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReconciliationLedger(string jobId, CancellationToken cancellationToken = default)
    {
        var ledger = await _repository.GetReconciliationLedgerAsync(jobId, cancellationToken);
        if (ledger == null)
        {
            return NotFound(new { error = $"Reconciliation ledger for Job '{jobId}' not found." });
        }
        return Ok(ledger);
    }

    /// <summary>
    /// Retrieves the review tasks flagged for human audit (low confidence, unverified evidence, or unresolved conflicts).
    /// </summary>
    /// <param name="jobId">The unique Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("jobs/{jobId}/review-queue")]
    [ProducesResponseType(typeof(List<ReviewTask>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewQueue(string jobId, CancellationToken cancellationToken = default)
    {
        var tasks = await _repository.GetReviewTasksAsync(jobId, cancellationToken);
        if (tasks == null)
        {
            return NotFound(new { error = $"Review tasks for Job '{jobId}' not found." });
        }
        return Ok(tasks);
    }
}
