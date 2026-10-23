using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Models;

/// <summary>
/// Top-level structured extraction result returned to the caller.
/// </summary>
public class DocumentAnalysisResult
{
    [JsonPropertyName("document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("overall_summary")]
    public string OverallSummary { get; set; } = string.Empty;

    [JsonPropertyName("total_pages_detected")]
    public int TotalPagesDetected { get; set; }

    [JsonPropertyName("overall_confidence")]
    public double OverallConfidence { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, FieldContent> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("tables")]
    public List<ExtractedTable>? Tables { get; set; } = new();
}

/// <summary>
/// Represents the content, confidence score, source page, and context of an extracted field in a key-value dictionary.
/// </summary>
public class FieldContent
{
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// Confidence score between 0.0 (lowest) and 1.0 (highest confidence).
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>
    /// 1-based page number where this information was found in the document.
    /// </summary>
    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    /// <summary>
    /// Exact quote or surrounding text snippet from the document page acting as evidence.
    /// </summary>
    [JsonPropertyName("evidence_context")]
    public string? EvidenceContext { get; set; }

    /// <summary>
    /// Returns a clean string representation of the value whether it is a string, number, or array.
    /// </summary>
    public string GetDisplayValue()
    {
        if (Value is null) return string.Empty;
        if (Value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return string.Join(", ", element.EnumerateArray().Select(e => e.ToString()));
            }
            return element.ToString();
        }
        if (Value is System.Collections.IEnumerable list && Value is not string)
        {
            var items = new List<string>();
            foreach (var item in list)
            {
                if (item != null) items.Add(item.ToString()!);
            }
            return string.Join(", ", items);
        }
        return Value.ToString() ?? string.Empty;
    }
}

/// <summary>
/// Represents a structured table detected in the document (e.g. invoice line items, financial breakdown).
/// </summary>
public class ExtractedTable
{
    [JsonPropertyName("table_name")]
    public string TableName { get; set; } = string.Empty;

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = new();

    [JsonPropertyName("rows")]
    public List<Dictionary<string, ExtractedCell>> Rows { get; set; } = new();
}

/// <summary>
/// Represents an individual cell within an extracted table with value and confidence.
/// </summary>
public class ExtractedCell
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

#region Gemini API Wire DTOs

internal class GeminiGenerateContentRequest
{
    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = new();

    [JsonPropertyName("systemInstruction")]
    public GeminiContent? SystemInstruction { get; set; }

    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig? GenerationConfig { get; set; }
}

internal class GeminiContent
{
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}

internal class GeminiPart
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("inlineData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiInlineData? InlineData { get; set; }
}

internal class GeminiInlineData
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty; // Base64 encoded bytes
}

internal class GeminiGenerationConfig
{
    [JsonPropertyName("responseMimeType")]
    public string ResponseMimeType { get; set; } = "application/json";

    [JsonPropertyName("responseSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ResponseSchema { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; } = 0.1;
}

internal class GeminiGenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("error")]
    public GeminiApiError? Error { get; set; }
}

internal class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

internal class GeminiApiError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

#endregion
