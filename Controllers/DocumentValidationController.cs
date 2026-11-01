using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Application.Validation;
using PixousDocIntelligence.Security;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Generic, configuration-driven document validation endpoints.
/// Inspects uploaded documents against client-defined requirements without application-specific hardcoding.
/// </summary>
[ApiController]
[Route("api/documents")]
public class DocumentValidationController : ControllerBase
{
    private readonly IValidationOrchestrator _orchestrator;
    private readonly IValidationProfileService _profileService;
    private readonly ILogger<DocumentValidationController> _logger;

    public DocumentValidationController(
        IValidationOrchestrator orchestrator,
        IValidationProfileService profileService,
        ILogger<DocumentValidationController> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates an uploaded document (PDF or image) against generic validation requirements.
    /// Requirements can be passed inline via 'requirementsJson' or referenced by 'profileId'.
    /// </summary>
    /// <param name="upload">The multipart form payload containing the document file and validation criteria.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured validation decision with check breakdown and evidence.</returns>
    [HttpPost("validate")]
    [Consumes("multipart/form-data")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
    [ProducesResponseType(typeof(ValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ValidateDocument(
        [FromForm] ValidateDocumentUploadRequest upload,
        CancellationToken cancellationToken = default)
    {
        if (upload == null)
        {
            return BadRequest(new { error = "Upload payload cannot be null." });
        }

        if (upload.File == null || upload.File.Length == 0)
        {
            return BadRequest(new { error = "A non-empty document file must be uploaded." });
        }

        if (string.IsNullOrWhiteSpace(upload.RequirementsJson) && string.IsNullOrWhiteSpace(upload.ProfileId))
        {
            return BadRequest(new { error = "Either 'requirementsJson' (inline requirements) or 'profileId' (saved profile) must be provided." });
        }

        string tenantId = ResolveTenantId();
        string? apiKey = Request.Headers["X-LLM-ApiKey"].FirstOrDefault();
        string? model = Request.Headers["X-LLM-Model"].FirstOrDefault();

        try
        {
            var response = await _orchestrator.ValidateAsync(upload, tenantId, apiKey, model, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException argEx)
        {
            return BadRequest(new { error = argEx.Message });
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429"))
        {
            _logger.LogWarning(httpEx, "Gemini rate limit or quota exceeded during document validation.");
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "LLM API rate limit or quota exceeded (HTTP 429). Please wait before retrying or provide a customer key via X-LLM-ApiKey.",
                details = httpEx.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred during generic document validation.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "An error occurred while validating the document.",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Retrieves a historical validation result by validationId.
    /// Supports both GET /api/validation/{validationId} and GET /api/documents/validation/{validationId}.
    /// </summary>
    /// <param name="validationId">The validation ID returned during document validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("validation/{validationId}")]
    [HttpGet("/api/validation/{validationId}")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetValidationResult(
        string validationId,
        CancellationToken cancellationToken = default)
    {
        string tenantId = ResolveTenantId();
        var result = await _profileService.GetValidationResultAsync(validationId, tenantId, cancellationToken);
        if (result == null)
        {
            return NotFound(new { error = $"Validation record '{validationId}' not found." });
        }

        return Ok(result);
    }

    private string ResolveTenantId()
    {
        return User.FindFirst("account_id")?.Value
            ?? User.Identity?.Name
            ?? Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? "default-tenant";
    }
}
