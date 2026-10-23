using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Domain.Contracts;

/// <summary>
/// Fixed outer envelope that encapsulates extraction candidates for a given document batch or stitched job.
/// This contract remains stable across all document types.
/// </summary>
public class DocumentEnvelope
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("document_summary")]
    public string DocumentSummary { get; set; } = string.Empty;

    [JsonPropertyName("document_keywords")]
    public List<string> DocumentKeywords { get; set; } = new();

    /// <summary>
    /// Stitched, deduplicated page-level Markdown representation for every page in the document.
    /// </summary>
    [JsonPropertyName("pages")]
    public List<PageMarkdownContent> Pages { get; set; } = new();

    /// <summary>
    /// Complete concatenated Markdown transcript of the document.
    /// </summary>
    [JsonPropertyName("full_markdown")]
    public string FullMarkdown { get; set; } = string.Empty;

    /// <summary>
    /// Batch-level breakdown including page ranges, batch summaries, and keywords.
    /// </summary>
    [JsonPropertyName("batches")]
    public List<BatchMarkdownResult> Batches { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ExtractedItem> Items { get; set; } = new();

    [JsonPropertyName("tables")]
    public List<ExtractedTableItem> Tables { get; set; } = new();

    [JsonPropertyName("unresolved_conflicts")]
    public List<ConflictRecord> UnresolvedConflicts { get; set; } = new();
}

/// <summary>
/// Verbatim Markdown representation of a single document page preserving headings, formatting, and tables.
/// </summary>
public class PageMarkdownContent
{
    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("has_tables")]
    public bool HasTables { get; set; }

    [JsonPropertyName("markdown")]
    public string Markdown { get; set; } = string.Empty;

    [JsonPropertyName("word_count")]
    public int WordCount => string.IsNullOrWhiteSpace(Markdown) ? 0 : Markdown.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>
/// Result of an individual 4-page batch extraction containing page-level markdowns, keywords, and summary.
/// </summary>
public class BatchMarkdownResult
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("start_page")]
    public int StartPage { get; set; }

    [JsonPropertyName("end_page")]
    public int EndPage { get; set; }

    [JsonPropertyName("batch_summary")]
    public string BatchSummary { get; set; } = string.Empty;

    [JsonPropertyName("batch_keywords")]
    public List<string> BatchKeywords { get; set; } = new();

    [JsonPropertyName("pages")]
    public List<PageMarkdownContent> Pages { get; set; } = new();
}

/// <summary>
/// Represents a single extracted item (field, heading, paragraph, or key-value pair).
/// </summary>
public class ExtractedItem
{
    [JsonPropertyName("item_type")]
    public string ItemType { get; set; } = "field"; // "field", "heading", "paragraph", "metric"

    [JsonPropertyName("field_key")]
    public string FieldKey { get; set; } = string.Empty;

    [JsonPropertyName("field_label")]
    public string FieldLabel { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// Normalized form of <see cref="Value"/> per the plan's NormalizationRules (e.g. a date
    /// rewritten to YYYY-MM-DD), kept alongside the original verbatim value rather than replacing it.
    /// Null when no normalization rule applied to this value.
    /// </summary>
    [JsonPropertyName("normalized_value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? NormalizedValue { get; set; }

    [JsonPropertyName("value_type")]
    public string ValueType { get; set; } = "string"; // "string", "number", "date", "currency", "boolean", "array"

    [JsonPropertyName("sources")]
    public List<SourceEvidence> Sources { get; set; } = new();

    /// <summary>
    /// Earliest page among all evidence sources backing this item. Used for batch-overlap
    /// grouping and simple page-number display; prefer <see cref="Sources"/> for full provenance.
    /// </summary>
    [JsonIgnore]
    public int PrimaryPageNumber => Sources.Count > 0 ? Sources.Min(s => s.PageNumber) : 0;

    /// <summary>
    /// All source quotations joined for display; prefer <see cref="Sources"/> for per-page evidence.
    /// </summary>
    [JsonIgnore]
    public string PrimarySourceText => string.Join(" | ", Sources.Select(s => s.SourceText).Where(t => !string.IsNullOrWhiteSpace(t)));

    [JsonPropertyName("confidence")]
    public ConfidenceSignal Confidence { get; set; } = new();

    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; set; } = "pending"; // "pending", "valid", "flagged", "rejected"

    /// <summary>
    /// True when this field's key did not match any planned field or alias and was instead
    /// discovered by the model on its own (e.g. a contract ID or payment terms clause that
    /// wasn't in the user's requested field list).
    /// </summary>
    [JsonPropertyName("is_dynamically_discovered")]
    public bool IsDynamicallyDiscovered { get; set; }

    /// <summary>
    /// Helper to get a string display value safely.
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
/// Verifiable source provenance linking an extracted item to its location in the PDF.
/// </summary>
public class SourceEvidence
{
    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("source_text")]
    public string SourceText { get; set; } = string.Empty;

    [JsonPropertyName("bounding_box")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundingBox? BoundingBox { get; set; }
}

public class BoundingBox
{
    [JsonPropertyName("top")]
    public double Top { get; set; }
    [JsonPropertyName("left")]
    public double Left { get; set; }
    [JsonPropertyName("width")]
    public double Width { get; set; }
    [JsonPropertyName("height")]
    public double Height { get; set; }
}

/// <summary>
/// Explicit confidence signal tracking both the quantitative score and its origin.
/// </summary>
public class ConfidenceSignal
{
    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "model_reported"; // "model_reported", "rule_verified", "calibrated"

    [JsonPropertyName("calibration_adjustment")]
    public double? CalibrationAdjustment { get; set; }
}

/// <summary>
/// Preserves tabular structures with row/column fidelity instead of flattening.
/// </summary>
public class ExtractedTableItem
{
    [JsonPropertyName("table_key")]
    public string TableKey { get; set; } = string.Empty;

    [JsonPropertyName("table_title")]
    public string? TableTitle { get; set; }

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("headers")]
    public List<string> Headers { get; set; } = new();

    [JsonPropertyName("rows")]
    public List<Dictionary<string, ExtractedCellItem>> Rows { get; set; } = new();
}

public class ExtractedCellItem
{
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("normalized_value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? NormalizedValue { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_text")]
    public string? SourceText { get; set; }
}

/// <summary>
/// Represents a preserved contradiction between competing observations from different batches or pages.
/// </summary>
public class ConflictRecord
{
    [JsonPropertyName("conflict_id")]
    public string ConflictId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("field_key")]
    public string FieldKey { get; set; } = string.Empty;

    [JsonPropertyName("candidates")]
    public List<CandidateObservation> Candidates { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unresolved"; // "unresolved", "human_reviewed", "rule_resolved"

    [JsonPropertyName("resolution_rationale")]
    public string? ResolutionRationale { get; set; }

    [JsonPropertyName("resolved_value")]
    public object? ResolvedValue { get; set; }
}

public class CandidateObservation
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_text")]
    public string SourceText { get; set; } = string.Empty;
}
