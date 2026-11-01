using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.DocumentGeneration.Models;
using PixousDocIntelligence.DocumentGeneration.Services;
using PixousDocIntelligence.Security;
using PixousDocIntelligence.Services.LLM;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Template-based AI document generation API pipeline supporting Word (.docx) and PDF outputs.
/// </summary>
[ApiController]
[Route("api/documents")]
[Tags("Document Generation (AI Pipeline)")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
public class DocumentGenerationController : ControllerBase
{
    private readonly IDocumentGenerationPipeline _pipeline;
    private readonly ILLMClientFactory _llmClientFactory;

    public DocumentGenerationController(
        IDocumentGenerationPipeline pipeline,
        ILLMClientFactory llmClientFactory)
    {
        _pipeline = pipeline;
        _llmClientFactory = llmClientFactory;
    }

    private string ResolveAccountId() =>
        User.FindFirst("account_id")?.Value
        ?? User.FindFirst("client_id")?.Value
        ?? "default_account";

    /// <summary>
    /// Generates structured content from user instructions and template schema.
    /// Distinguishes AI-drafted content from missing mandatory user facts.
    /// </summary>
    [HttpPost("draft")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(DraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CreateDraft([FromBody] DraftRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var llmClient = _llmClientFactory.GetClient(request.Provider, request.ApiKey, request.Model);
            string accountId = ResolveAccountId();

            var response = await _pipeline.CreateDraftAsync(request, accountId, llmClient, cancellationToken);
            return Ok(response);
        }
        catch (DocumentPipelineException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
        catch (ContentGenerationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Validates the current document draft and optionally applies user updates for missing fields.
    /// </summary>
    [HttpPost("{id}/validate")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(ValidateDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateDraft(string id, [FromBody] ValidateDraftRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _pipeline.ValidateDraftAsync(id, request, cancellationToken);
            return Ok(response);
        }
        catch (DocumentPipelineException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Previews the current draft content and intermediate markdown.
    /// </summary>
    [HttpGet("{id}/preview")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(DraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDraftPreview(string id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _pipeline.GetDraftPreviewAsync(id, cancellationToken);
            return Ok(response);
        }
        catch (DocumentPipelineException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Renders the validated document into DOCX, PDF, or both formats.
    /// </summary>
    [HttpPost("{id}/generate")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(GenerateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateDocument(string id, [FromBody] GenerateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _pipeline.GenerateDocumentsAsync(id, request, cancellationToken);
            return Ok(response);
        }
        catch (DocumentPipelineException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Downloads the rendered document file in the requested format (docx or pdf).
    /// </summary>
    [HttpGet("{id}/download")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(string id, [FromQuery] string format = "docx", CancellationToken cancellationToken = default)
    {
        try
        {
            var (bytes, contentType, fileName) = await _pipeline.DownloadDocumentAsync(id, format, cancellationToken);
            return File(bytes, contentType, fileName);
        }
        catch (DocumentPipelineException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }
}
