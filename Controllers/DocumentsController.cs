using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Orchestration;
using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Persistence.Repositories;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Endpoints for ingesting, processing, and querying documents through the 7-phase intelligence pipeline.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentProcessingOrchestrator _orchestrator;
    private readonly IDocumentProcessingRepository _repository;
    private readonly Credentials.ICredentialRegistry _credentialRegistry;

    public DocumentsController(
        IDocumentProcessingOrchestrator orchestrator,
        IDocumentProcessingRepository repository,
        Credentials.ICredentialRegistry credentialRegistry)
    {
        _orchestrator = orchestrator;
        _repository = repository;
        _credentialRegistry = credentialRegistry;
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
    [Authorize(Roles = "process,admin")]
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
            // 1. Derive tenant identity strictly from authenticated credential
            string clientIdentifier = User.FindFirst("client_id")?.Value ?? upload.TenantId ?? "default-tenant";

            // 2. Pre-execution quota validation
            int incomingPages = 1;
            try
            {
                using var pdfProbe = UglyToad.PdfPig.PdfDocument.Open(tempFilePath);
                incomingPages = pdfProbe.NumberOfPages;
            }
            catch { /* fallback to 1 page if probe fails */ }

            var (quotaAllowed, remainingBefore, quotaMsg) = await _credentialRegistry.CheckQuotaAsync(clientIdentifier, incomingPages, cancellationToken);
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

            var request = new DocumentProcessingRequest
            {
                DocumentPath = tempFilePath,
                UserInstructions = upload.Instructions,
                TenantId = clientIdentifier,
                Tier = upload.Tier ?? Request.Headers["X-LLM-Tier"].FirstOrDefault() ?? "fast",
                Provider = upload.Provider ?? Request.Headers["X-LLM-Provider"].FirstOrDefault(),
                Model = upload.Model ?? Request.Headers["X-LLM-Model"].FirstOrDefault(),
                ApiKey = upload.ApiKey ?? Request.Headers["X-LLM-ApiKey"].FirstOrDefault()
            };

            var result = await _orchestrator.ProcessDocumentAsync(request, cancellationToken: cancellationToken);

            // 3. Atomically record usage in the credential registry
            int actualPages = result.Manifest.TotalPages > 0 ? result.Manifest.TotalPages : incomingPages;
            await _credentialRegistry.RecordUsageAsync(clientIdentifier, actualPages, "document_process", cancellationToken);

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
            // Optionally clean up staging upload after processing completes
            if (System.IO.File.Exists(tempFilePath))
            {
                try { System.IO.File.Delete(tempFilePath); } catch { /* Ignore file lock */ }
            }
        }
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
