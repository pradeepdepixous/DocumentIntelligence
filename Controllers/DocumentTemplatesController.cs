using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.DocumentGeneration.Models;
using PixousDocIntelligence.DocumentGeneration.Repositories;
using PixousDocIntelligence.DocumentGeneration.Validation;
using PixousDocIntelligence.Security;

namespace PixousDocIntelligence.Controllers;

[ApiController]
[Route("api/templates")]
[Tags("Document Templates")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
public class DocumentTemplatesController : ControllerBase
{
    private readonly ITemplateRepository _templateRepo;
    private readonly ITemplateValidator _templateValidator;
    private readonly PixousDocIntelligence.DocumentGeneration.Services.IDocxTemplateParser _docxParser;

    public DocumentTemplatesController(
        ITemplateRepository templateRepo, 
        ITemplateValidator templateValidator,
        PixousDocIntelligence.DocumentGeneration.Services.IDocxTemplateParser docxParser)
    {
        _templateRepo = templateRepo;
        _templateValidator = templateValidator;
        _docxParser = docxParser;
    }

    /// <summary>
    /// Lists all registered document templates.
    /// </summary>
    [HttpGet]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(IEnumerable<DocumentTemplateDefinition>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTemplates(CancellationToken cancellationToken)
    {
        var templates = await _templateRepo.ListTemplatesAsync(cancellationToken);
        return Ok(templates);
    }

    /// <summary>
    /// Retrieves a document template definition by ID.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(DocumentTemplateDefinition), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplate(string id, [FromQuery] int? version, CancellationToken cancellationToken)
    {
        var template = await _templateRepo.GetTemplateAsync(id, version, cancellationToken);
        if (template == null)
        {
            return NotFound(new { error = $"Template '{id}' not found." });
        }
        return Ok(template);
    }

    /// <summary>
    /// Registers or updates a document template definition via JSON.
    /// </summary>
    [HttpPost]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(DocumentTemplateDefinition), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterTemplate([FromBody] DocumentTemplateDefinition template, CancellationToken cancellationToken)
    {
        var validation = _templateValidator.Validate(template);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = "Invalid template definition", details = validation.Errors });
        }

        await _templateRepo.SaveTemplateAsync(template, cancellationToken);
        return CreatedAtAction(nameof(GetTemplate), new { id = template.TemplateId }, template);
    }

    /// <summary>
    /// Inspects an uploaded Word (.docx) template and returns proposed fields and structure without saving.
    /// </summary>
    [HttpPost("inspect")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PixousDocIntelligence.DocumentGeneration.Services.TemplateInspectionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InspectDocxTemplate(
        IFormFile file,
        [FromForm] string? template_id,
        [FromForm] string? title,
        [FromForm] string? description,
        [FromForm] string? document_type,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "A valid .docx file must be uploaded for inspection." });
        }

        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only .docx Word documents are supported." });
        }

        string rawName = Path.GetFileNameWithoutExtension(file.FileName);
        string templateId = !string.IsNullOrWhiteSpace(template_id) 
            ? template_id.Trim().ToLowerInvariant().Replace(" ", "_")
            : rawName.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        var inspection = _docxParser.ParseAndInspect(memoryStream, templateId, title, description, document_type);
        return Ok(inspection);
    }

    /// <summary>
    /// Uploads an arbitrary Word (.docx) template with optional administrator-configured schema overrides.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DocumentTemplateDefinition), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadDocxTemplate(
        IFormFile file,
        [FromForm] string? template_id,
        [FromForm] string? title,
        [FromForm] string? description,
        [FromForm] string? document_type,
        [FromForm] string? schema_json,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "A valid .docx file must be uploaded." });
        }

        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only .docx Word documents are supported for template uploads." });
        }

        string rawName = Path.GetFileNameWithoutExtension(file.FileName);
        string templateId = !string.IsNullOrWhiteSpace(template_id) 
            ? template_id.Trim().ToLowerInvariant().Replace(" ", "_")
            : rawName.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        byte[] docxBytes = memoryStream.ToArray();

        memoryStream.Position = 0;
        var inspection = _docxParser.ParseAndInspect(memoryStream, templateId, title, description, document_type);

        if (!inspection.IsValid)
        {
            return BadRequest(new { error = "Failed to inspect uploaded Word template", details = inspection.Errors });
        }

        DocumentTemplateDefinition finalTemplate = inspection.Template;

        // If administrator provided custom schema overrides, apply them
        if (!string.IsNullOrWhiteSpace(schema_json))
        {
            try
            {
                var customSchema = System.Text.Json.JsonSerializer.Deserialize<TemplateContentSchema>(schema_json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (customSchema != null && customSchema.Sections != null && customSchema.Sections.Count > 0)
                {
                    finalTemplate.ContentSchema = customSchema;
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "Invalid schema_json provided", details = ex.Message });
            }
        }

        // Validate template schema
        var schemaValidation = _templateValidator.Validate(finalTemplate);
        if (!schemaValidation.IsValid)
        {
            return BadRequest(new { error = "Template schema validation failed", details = schemaValidation.Errors });
        }

        // Persist both the parsed template schema and raw .docx file
        await _templateRepo.SaveTemplateAsync(finalTemplate, cancellationToken);
        await _templateRepo.SaveTemplateDocxAsync(templateId, docxBytes, cancellationToken);

        return CreatedAtAction(nameof(GetTemplate), new { id = templateId }, finalTemplate);
    }

    /// <summary>
    /// Updates the configuration schema for an existing template (types, descriptions, required flags).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined, Roles = "generate,admin")]
    [ProducesResponseType(typeof(DocumentTemplateDefinition), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTemplate(string id, [FromBody] DocumentTemplateDefinition template, CancellationToken cancellationToken)
    {
        var existing = await _templateRepo.GetTemplateAsync(id, cancellationToken: cancellationToken);
        if (existing == null)
        {
            return NotFound(new { error = $"Template '{id}' not found." });
        }

        template.TemplateId = id;
        var validation = _templateValidator.Validate(template);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = "Invalid template schema", details = validation.Errors });
        }

        await _templateRepo.SaveTemplateAsync(template, cancellationToken);
        return Ok(template);
    }

    /// <summary>
    /// Downloads the raw Word (.docx) template file for a given template ID.
    /// </summary>
    [HttpGet("{id}/download")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadTemplateDocx(string id, CancellationToken cancellationToken)
    {
        byte[]? docxBytes = await _templateRepo.GetTemplateDocxAsync(id, cancellationToken);
        if (docxBytes == null || docxBytes.Length == 0)
        {
            return NotFound(new { error = $"Template file for '{id}' not found." });
        }

        string fileName = $"{id}.docx";
        return File(docxBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
    }
}

