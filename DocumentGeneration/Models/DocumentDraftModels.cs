using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixousDocIntelligence.DocumentGeneration.Models;

/// <summary>
/// User request to draft document content from plain instructions and template definition or ID.
/// </summary>
public class DraftRequest
{
    [JsonPropertyName("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("template_version")]
    public int? TemplateVersion { get; set; }

    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = string.Empty;

    [JsonPropertyName("output_format")]
    public string OutputFormat { get; set; } = "docx";

    [JsonPropertyName("template_definition")]
    public DocumentTemplateDefinition? TemplateDefinition { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

/// <summary>
/// Structured document content produced by the AI content generator and content validator.
/// </summary>
public class StructuredDocumentContent
{
    [JsonPropertyName("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("template_version")]
    public int TemplateVersion { get; set; } = 1;

    [JsonPropertyName("content")]
    public Dictionary<string, JsonElement> Content { get; set; } = new();

    [JsonPropertyName("missing_fields")]
    public List<string> MissingFields { get; set; } = new();
}

/// <summary>
/// Response returned by POST /api/documents/draft
/// </summary>
public class DraftResponse
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Status: "needs_information", "ready", "generated"
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "needs_information";

    [JsonPropertyName("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("template_version")]
    public int TemplateVersion { get; set; } = 1;

    [JsonPropertyName("missing_fields")]
    public List<string> MissingFields { get; set; } = new();

    [JsonPropertyName("content")]
    public Dictionary<string, JsonElement> Content { get; set; } = new();

    [JsonPropertyName("markdown_preview")]
    public string? MarkdownPreview { get; set; }
}

public class ValidateDraftRequest
{
    [JsonPropertyName("content")]
    public Dictionary<string, JsonElement>? UpdatedContent { get; set; }
}

public class ValidateDraftResponse
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "needs_information";

    [JsonPropertyName("missing_fields")]
    public List<string> MissingFields { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

public class GenerateRequest
{
    [JsonPropertyName("formats")]
    public List<string> Formats { get; set; } = new() { "docx", "pdf" };
}

public class GenerateResponse
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "generated";

    [JsonPropertyName("files")]
    public List<GeneratedFileInfo> Files { get; set; } = new();
}

public class GeneratedFileInfo
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;
}

public class DocumentDraftRecord
{
    public string DocumentId { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int TemplateVersion { get; set; } = 1;
    public string Instructions { get; set; } = string.Empty;
    public string Status { get; set; } = "needs_information";
    public Dictionary<string, JsonElement> Content { get; set; } = new();
    public List<string> MissingFields { get; set; } = new();
    public string? GeneratedMarkdown { get; set; }
    public Dictionary<string, string> GeneratedFilePaths { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
