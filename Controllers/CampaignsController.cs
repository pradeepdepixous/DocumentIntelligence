using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Application.Jobs;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Persistence.Repositories;
using PixousDocIntelligence.Security;
using PixousDocIntelligence.Services.Accounts;
using PixousDocIntelligence.Services.Campaigns;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// High-throughput Campaign Management for batch processing up to 300 procurement/bidding documents.
/// Freezes a shared extraction schema, schedules jobs across a bounded-concurrency worker pool,
/// and stores each final verified bid JSON separately on disk.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
public class CampaignsController : ControllerBase
{
    private readonly IDocumentJobQueue _jobQueue;
    private readonly ICampaignStorageService _campaignStorage;
    private readonly IDocumentProcessingRepository _repository;
    private readonly Credentials.ICredentialRegistry _credentialRegistry;
    private readonly CustomerAccountService _accountService;

    public CampaignsController(
        IDocumentJobQueue jobQueue,
        ICampaignStorageService campaignStorage,
        IDocumentProcessingRepository repository,
        Credentials.ICredentialRegistry credentialRegistry,
        CustomerAccountService accountService)
    {
        _jobQueue = jobQueue;
        _campaignStorage = campaignStorage;
        _repository = repository;
        _credentialRegistry = credentialRegistry;
        _accountService = accountService;
    }

    /// <summary>
    /// Ingests a bulk batch of up to 300 procurement/bidding documents, freezes the shared JSON schema,
    /// and enqueues all jobs for asynchronous processing with bounded concurrency.
    /// Returns 202 Accepted immediately with campaign tracking URLs.
    /// </summary>
    [HttpPost("bids/batch")]
    [Consumes("multipart/form-data")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "process,admin")]
    [ProducesResponseType(typeof(CampaignAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitBidsBatch(
        [FromForm] CreateCampaignBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Files == null || request.Files.Count == 0)
        {
            return BadRequest(new { error = "At least one bid PDF document must be uploaded." });
        }

        // 1. Resolve identity
        string? clientId = User.FindFirst("client_id")?.Value;
        string? sessionAccountId = User.FindFirst("account_id")?.Value;
        string? billedAccountId = sessionAccountId;

        if (clientId != null)
        {
            var credential = await _credentialRegistry.GetClientByIdAsync(clientId, cancellationToken);
            billedAccountId = credential?.OwnerAccountId;
        }

        string clientIdentifier = clientId ?? sessionAccountId ?? "default-tenant";

        // 2. Resolve and parse shared manual schema ONCE for all 300 bids
        string? manualSchema = request.Schema;
        if (string.IsNullOrWhiteSpace(manualSchema) && request.SchemaFile != null && request.SchemaFile.Length > 0)
        {
            using var reader = new StreamReader(request.SchemaFile.OpenReadStream());
            manualSchema = await reader.ReadToEndAsync(cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(manualSchema))
        {
            manualSchema = Request.Headers["X-Extraction-Schema"].FirstOrDefault();
        }

        ExtractionPlan? frozenPlan = null;
        if (!string.IsNullOrWhiteSpace(manualSchema))
        {
            try
            {
                frozenPlan = SchemaParser.Parse(manualSchema, request.Instructions);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "InvalidSchemaJson", message = ex.Message });
            }
        }

        // 3. Initialize Campaign & Directory Structure
        string campaignId = $"CMP-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        string campaignName = request.CampaignName ?? $"Bidding Campaign {DateTime.UtcNow:yyyy-MM-dd HH:mm}";

        await _campaignStorage.InitializeCampaignAsync(
            campaignId, 
            campaignName, 
            request.Files.Count, 
            frozenPlan, 
            cancellationToken);

        string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "campaigns", campaignId);
        Directory.CreateDirectory(uploadsDir);

        // 4. Stagger and enqueue all bid documents
        foreach (var file in request.Files)
        {
            if (file == null || file.Length == 0) continue;

            string bidId = $"BID-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            string jobId = $"JOB-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            string documentId = $"DOC-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

            string tempFilePath = Path.Combine(uploadsDir, $"{bidId}_{file.FileName}");
            await using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            int incomingPages = 1;
            try
            {
                using var pdfProbe = UglyToad.PdfPig.PdfDocument.Open(tempFilePath);
                incomingPages = pdfProbe.NumberOfPages;
            }
            catch { /* probe fallback */ }

            // Create initial entities in repository
            var docEntity = new DocumentEntity
            {
                DocumentId = documentId,
                TenantId = clientIdentifier,
                FileName = file.FileName,
                FilePath = Path.GetFullPath(tempFilePath),
                FileSizeBytes = file.Length,
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
                UserInstruction = request.Instructions,
                ExtractionPlanJson = frozenPlan != null ? System.Text.Json.JsonSerializer.Serialize(frozenPlan) : string.Empty,
                SchemaVersion = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _repository.SaveJobAsync(jobEntity, cancellationToken);

            await _campaignStorage.RegisterBidQueuedAsync(
                campaignId, 
                bidId, 
                jobId, 
                documentId, 
                file.FileName, 
                cancellationToken);

            var workItem = new DocumentJobWorkItem
            {
                JobId = jobId,
                DocumentId = documentId,
                CampaignId = campaignId,
                BidId = bidId,
                SourceFileName = file.FileName,
                Request = new DocumentProcessingRequest
                {
                    JobId = jobId,
                    DocumentId = documentId,
                    DocumentPath = tempFilePath,
                    UserInstructions = request.Instructions,
                    TenantId = clientIdentifier,
                    Tier = request.Tier ?? "fast",
                    Mode = request.Mode ?? "schema",
                    Schema = manualSchema // Each bid runs against the pre-frozen schema
                },
                ClientIdentifier = clientIdentifier,
                BilledAccountId = billedAccountId,
                ClientId = clientId,
                IncomingPages = incomingPages,
                TempFilePath = tempFilePath
            };

            await _jobQueue.EnqueueAsync(workItem, cancellationToken);
        }

        Response.Headers["Location"] = $"/api/campaigns/bids/{campaignId}/status";
        return StatusCode(StatusCodes.Status202Accepted, new CampaignAcceptedResponse
        {
            CampaignId = campaignId,
            CampaignName = campaignName,
            Status = "Processing",
            TotalDocuments = request.Files.Count,
            QueuedAt = DateTime.UtcNow,
            StatusUrl = $"/api/campaigns/bids/{campaignId}/status",
            BidsUrl = $"/api/campaigns/bids/{campaignId}/bids",
            StorageDirectory = Path.Combine("output", "campaigns", campaignId, "bids")
        });
    }

    /// <summary>
    /// Retrieves live batch campaign progress, completed/failed counts, and bid summary items.
    /// </summary>
    [HttpGet("bids/{campaignId}/status")]
    [ProducesResponseType(typeof(CampaignStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCampaignStatus(string campaignId, CancellationToken cancellationToken = default)
    {
        var status = await _campaignStorage.GetCampaignStatusAsync(campaignId, cancellationToken);
        if (status == null)
        {
            return NotFound(new { error = $"Campaign '{campaignId}' not found." });
        }
        return Ok(status);
    }

    /// <summary>
    /// Retrieves all isolated bid JSON records for a completed or in-progress campaign.
    /// </summary>
    [HttpGet("bids/{campaignId}/bids")]
    [ProducesResponseType(typeof(List<IsolatedBidRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCampaignBids(string campaignId, CancellationToken cancellationToken = default)
    {
        var bids = await _campaignStorage.GetCampaignBidsAsync(campaignId, cancellationToken);
        return Ok(bids);
    }

    /// <summary>
    /// Retrieves the single isolated JSON record for a specific bid document.
    /// </summary>
    [HttpGet("bids/{campaignId}/bids/{bidId}")]
    [ProducesResponseType(typeof(IsolatedBidRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSingleBid(string campaignId, string bidId, CancellationToken cancellationToken = default)
    {
        var bid = await _campaignStorage.GetBidRecordAsync(campaignId, bidId, cancellationToken);
        if (bid == null)
        {
            return NotFound(new { error = $"Bid '{bidId}' in Campaign '{campaignId}' not found." });
        }
        return Ok(bid);
    }
}
