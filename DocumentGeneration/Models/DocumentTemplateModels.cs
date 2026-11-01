using System.Text.Json.Serialization;

namespace PixousDocIntelligence.DocumentGeneration.Models;

/// <summary>
/// Defines a document template contract: document sections, fields, constraints, and presentation.
/// </summary>
public class DocumentTemplateDefinition
{
    [JsonPropertyName("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("document_type")]
    public string DocumentType { get; set; } = "document";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("content_schema")]
    public TemplateContentSchema ContentSchema { get; set; } = new();

    [JsonPropertyName("presentation")]
    public TemplatePresentation Presentation { get; set; } = new();
}

public class TemplateContentSchema
{
    [JsonPropertyName("sections")]
    public List<TemplateSectionDefinition> Sections { get; set; } = new();
}

public class TemplateSectionDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Type of section: "address_block", "date", "text", "paragraphs", "signature_block", "table", "list", "bullet_list", "object".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("fields")]
    public List<string>? Fields { get; set; }
}

public class TemplatePresentation
{
    [JsonPropertyName("reference_docx")]
    public string? ReferenceDocx { get; set; }

    [JsonPropertyName("page_size")]
    public string PageSize { get; set; } = "A4";

    [JsonPropertyName("font_family")]
    public string FontFamily { get; set; } = "Arial";

    [JsonPropertyName("primary_color")]
    public string PrimaryColor { get; set; } = "#1E293B";

    [JsonPropertyName("margins")]
    public PageMargins Margins { get; set; } = new();
}

public class PageMargins
{
    [JsonPropertyName("top")]
    public float Top { get; set; } = 20f;

    [JsonPropertyName("bottom")]
    public float Bottom { get; set; } = 20f;

    [JsonPropertyName("left")]
    public float Left { get; set; } = 20f;

    [JsonPropertyName("right")]
    public float Right { get; set; } = 20f;
}
