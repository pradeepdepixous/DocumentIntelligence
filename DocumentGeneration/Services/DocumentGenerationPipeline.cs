using System.Text.Json;
using Microsoft.Extensions.Logging;
using PixousDocIntelligence.DocumentGeneration.Models;
using PixousDocIntelligence.DocumentGeneration.Rendering;
using PixousDocIntelligence.DocumentGeneration.Repositories;
using PixousDocIntelligence.DocumentGeneration.Validation;
using PixousDocIntelligence.Services.LLM;

namespace PixousDocIntelligence.DocumentGeneration.Services;

public class DocumentPipelineException : Exception
{
    public int StatusCode { get; }
    public DocumentPipelineException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}

public interface IDocumentGenerationPipeline
{
    Task<DraftResponse> CreateDraftAsync(DraftRequest request, string accountId, ILLMClient llmClient, CancellationToken cancellationToken = default);
    Task<ValidateDraftResponse> ValidateDraftAsync(string documentId, ValidateDraftRequest? request, CancellationToken cancellationToken = default);
    Task<DraftResponse> GetDraftPreviewAsync(string documentId, CancellationToken cancellationToken = default);
    Task<GenerateResponse> GenerateDocumentsAsync(string documentId, GenerateRequest request, CancellationToken cancellationToken = default);
    Task<(byte[] Bytes, string ContentType, string FileName)> DownloadDocumentAsync(string documentId, string format, CancellationToken cancellationToken = default);
}

public class DocumentGenerationPipeline : IDocumentGenerationPipeline
{
    private readonly ITemplateRepository _templateRepo;
    private readonly IDocumentDraftRepository _draftRepo;
    private readonly ITemplateValidator _templateValidator;
    private readonly IContentValidator _contentValidator;
    private readonly IAiContentGenerator _aiGenerator;
    private readonly IMarkdownBuilder _markdownBuilder;
    private readonly IEnumerable<IDocumentRenderer> _renderers;
    private readonly IDocxTemplatePopulator _docxPopulator;
    private readonly ILogger<DocumentGenerationPipeline> _logger;

    public DocumentGenerationPipeline(
        ITemplateRepository templateRepo,
        IDocumentDraftRepository draftRepo,
        ITemplateValidator templateValidator,
        IContentValidator contentValidator,
        IAiContentGenerator aiGenerator,
        IMarkdownBuilder markdownBuilder,
        IEnumerable<IDocumentRenderer> renderers,
        IDocxTemplatePopulator docxPopulator,
        ILogger<DocumentGenerationPipeline> logger)
    {
        _templateRepo = templateRepo;
        _draftRepo = draftRepo;
        _templateValidator = templateValidator;
        _contentValidator = contentValidator;
        _aiGenerator = aiGenerator;
        _markdownBuilder = markdownBuilder;
        _renderers = renderers;
        _docxPopulator = docxPopulator;
        _logger = logger;
    }

    public async Task<DraftResponse> CreateDraftAsync(
        DraftRequest request, 
        string accountId, 
        ILLMClient llmClient, 
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        // 1. Resolve Template definition
        var template = request.TemplateDefinition;
        if (template == null)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateId))
            {
                throw new DocumentPipelineException("Either 'template_id' or inline 'template_definition' must be provided.", 400);
            }

            template = await _templateRepo.GetTemplateAsync(request.TemplateId, request.TemplateVersion, cancellationToken);
            if (template == null)
            {
                throw new DocumentPipelineException($"Template '{request.TemplateId}' was not found.", 404);
            }
        }

        // 2. Validate Template
        var templateValidation = _templateValidator.Validate(template);
        if (!templateValidation.IsValid)
        {
            throw new DocumentPipelineException($"Template validation failed: {string.Join("; ", templateValidation.Errors)}", 400);
        }

        // 3. AI Content Generator: Prompt LLM -> Structured JSON
        var structured = await _aiGenerator.GenerateContentAsync(template, request.Instructions, llmClient, cancellationToken);

        // Apply configured date policy (default to current date if not provided)
        ApplyDatePolicy(template, structured.Content);

        // 4. Content Validator: Schema check & missing fields
        var contentValidation = _contentValidator.ValidateContent(template, structured.Content, structured.MissingFields);

        // 5. Deterministic Markdown Builder
        string markdown = _markdownBuilder.BuildMarkdown(template, structured.Content);

        // 6. Save Draft
        string documentId = $"DOC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}";
        var draftRecord = new DocumentDraftRecord
        {
            DocumentId = documentId,
            AccountId = accountId,
            TemplateId = template.TemplateId,
            TemplateVersion = template.Version,
            Instructions = request.Instructions,
            Status = contentValidation.Status,
            Content = structured.Content,
            MissingFields = contentValidation.MissingMandatoryFields,
            GeneratedMarkdown = markdown
        };

        await _draftRepo.SaveDraftAsync(draftRecord, cancellationToken);

        return new DraftResponse
        {
            DocumentId = documentId,
            Status = draftRecord.Status,
            TemplateId = template.TemplateId,
            TemplateVersion = template.Version,
            MissingFields = draftRecord.MissingFields,
            Content = draftRecord.Content,
            MarkdownPreview = markdown
        };
    }

    public async Task<ValidateDraftResponse> ValidateDraftAsync(
        string documentId, 
        ValidateDraftRequest? request, 
        CancellationToken cancellationToken = default)
    {
        var draft = await _draftRepo.GetDraftAsync(documentId, cancellationToken);
        if (draft == null)
        {
            throw new DocumentPipelineException($"Document draft '{documentId}' was not found.", 404);
        }

        var template = await _templateRepo.GetTemplateAsync(draft.TemplateId, draft.TemplateVersion, cancellationToken);
        if (template == null)
        {
            throw new DocumentPipelineException($"Associated template '{draft.TemplateId}' was not found.", 404);
        }

        // If user submitted updated content fields (e.g. providing missing recipient address or signatory)
        if (request?.UpdatedContent != null)
        {
            foreach (var kvp in request.UpdatedContent)
            {
                draft.Content[kvp.Key] = kvp.Value;
            }
        }

        ApplyDatePolicy(template, draft.Content);

        var contentValidation = _contentValidator.ValidateContent(template, draft.Content);
        draft.Status = contentValidation.Status;
        draft.MissingFields = contentValidation.MissingMandatoryFields;
        draft.GeneratedMarkdown = _markdownBuilder.BuildMarkdown(template, draft.Content);

        await _draftRepo.SaveDraftAsync(draft, cancellationToken);

        return new ValidateDraftResponse
        {
            DocumentId = documentId,
            IsValid = contentValidation.IsValid,
            Status = draft.Status,
            MissingFields = draft.MissingFields,
            Errors = contentValidation.Errors
        };
    }

    public async Task<DraftResponse> GetDraftPreviewAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var draft = await _draftRepo.GetDraftAsync(documentId, cancellationToken);
        if (draft == null)
        {
            throw new DocumentPipelineException($"Document draft '{documentId}' was not found.", 404);
        }

        return new DraftResponse
        {
            DocumentId = draft.DocumentId,
            Status = draft.Status,
            TemplateId = draft.TemplateId,
            TemplateVersion = draft.TemplateVersion,
            MissingFields = draft.MissingFields,
            Content = draft.Content,
            MarkdownPreview = draft.GeneratedMarkdown
        };
    }

    public async Task<GenerateResponse> GenerateDocumentsAsync(
        string documentId, 
        GenerateRequest request, 
        CancellationToken cancellationToken = default)
    {
        var draft = await _draftRepo.GetDraftAsync(documentId, cancellationToken);
        if (draft == null)
        {
            throw new DocumentPipelineException($"Document draft '{documentId}' was not found.", 404);
        }

        var template = await _templateRepo.GetTemplateAsync(draft.TemplateId, draft.TemplateVersion, cancellationToken);
        if (template == null)
        {
            throw new DocumentPipelineException($"Template '{draft.TemplateId}' was not found.", 404);
        }

        string markdown = draft.GeneratedMarkdown ?? _markdownBuilder.BuildMarkdown(template, draft.Content);

        var response = new GenerateResponse
        {
            DocumentId = documentId,
            Status = "generated",
            Files = new List<GeneratedFileInfo>()
        };

        var requestedFormats = request.Formats != null && request.Formats.Count > 0 
            ? request.Formats 
            : new List<string> { "docx", "pdf" };

        foreach (var format in requestedFormats)
        {
            string fmt = format.Trim().ToLowerInvariant();
            var renderer = _renderers.FirstOrDefault(r => r.Format.Equals(fmt, StringComparison.OrdinalIgnoreCase));
            if (renderer == null)
            {
                throw new DocumentPipelineException($"Output format '{format}' is not supported. Supported: docx, pdf.", 400);
            }

            try
            {
                byte[] bytes;
                byte[]? templateDocx = await _templateRepo.GetTemplateDocxAsync(template.TemplateId, cancellationToken);
                if (fmt.Equals("docx", StringComparison.OrdinalIgnoreCase) && templateDocx != null && templateDocx.Length > 0)
                {
                    _logger.LogInformation("Populating uploaded Word template '{TemplateId}' for document '{DocumentId}'", template.TemplateId, documentId);
                    bytes = _docxPopulator.PopulateDocx(templateDocx, template, draft.Content);
                }
                else
                {
                    bytes = renderer.Render(template, draft.Content, markdown);
                }

                await _draftRepo.SaveGeneratedFileAsync(documentId, fmt, bytes, cancellationToken);

                response.Files.Add(new GeneratedFileInfo
                {
                    Format = fmt,
                    DownloadUrl = $"/api/documents/{documentId}/download?format={fmt}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to render format '{Format}' for document '{DocumentId}'", fmt, documentId);
                throw new DocumentPipelineException($"Failed to render {fmt}: {ex.Message}", 500);
            }
        }

        draft.Status = "generated";
        await _draftRepo.SaveDraftAsync(draft, cancellationToken);

        return response;
    }

    public async Task<(byte[] Bytes, string ContentType, string FileName)> DownloadDocumentAsync(
        string documentId, 
        string format, 
        CancellationToken cancellationToken = default)
    {
        string fmt = (format ?? "docx").Trim().ToLowerInvariant();
        byte[]? bytes = await _draftRepo.GetGeneratedFileAsync(documentId, fmt, cancellationToken);

        if (bytes == null || bytes.Length == 0)
        {
            throw new DocumentPipelineException($"Document '{documentId}' in format '{fmt}' has not been generated or was not found.", 404);
        }

        string contentType = fmt == "pdf" 
            ? "application/pdf" 
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        string fileName = $"{documentId}.{fmt}";

        return (bytes, contentType, fileName);
    }

    private static void ApplyDatePolicy(DocumentTemplateDefinition template, Dictionary<string, JsonElement> content)
    {
        foreach (var section in template.ContentSchema.Sections)
        {
            if (section.Type.Equals("date", StringComparison.OrdinalIgnoreCase) || section.Id.Equals("date", StringComparison.OrdinalIgnoreCase))
            {
                bool hasVal = content.TryGetValue(section.Id, out var val) 
                    && val.ValueKind == JsonValueKind.String 
                    && !string.IsNullOrWhiteSpace(val.GetString());

                if (!hasVal)
                {
                    content[section.Id] = JsonDocument.Parse($"\"{DateTime.UtcNow:yyyy-MM-dd}\"").RootElement;
                }
            }
        }
    }
}
