using System.Text.Json.Serialization;
using PixousDocIntelligence.Domain.Contracts;

namespace PixousDocIntelligence.Pipeline.Verification;

/// <summary>
/// Status of an item's evidence verification check against the physical PDF page text or vision model.
/// </summary>
public enum EvidenceCheckStatus
{
    Verified,
    FuzzyMatch,
    VisionVerifiedScannedPage,
    MissingFromPage,
    PageOutOfRange
}

/// <summary>
/// Result of verifying extracted items against source PDF text, scanned page layers, and applying quality policies.
/// </summary>
public class VerificationReport
{
    [JsonPropertyName("total_items_checked")]
    public int TotalItemsChecked { get; set; }

    [JsonPropertyName("verified_count")]
    public int VerifiedCount { get; set; }

    [JsonPropertyName("vision_verified_count")]
    public int VisionVerifiedCount { get; set; }

    [JsonPropertyName("scanned_page_count")]
    public int ScannedPageCount { get; set; }

    [JsonPropertyName("unverified_evidence_count")]
    public int UnverifiedEvidenceCount { get; set; }

    [JsonPropertyName("low_confidence_count")]
    public int LowConfidenceCount { get; set; }

    [JsonPropertyName("review_tasks")]
    public List<ReviewTask> ReviewTasks { get; set; } = new();

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    /// <summary>
    /// Per-page breakdown of how many extracted items and tables cited that page as evidence,
    /// so completeness can be checked explicitly instead of inferred from the item count alone.
    /// </summary>
    [JsonPropertyName("page_coverage")]
    public List<PageCoverage> PageCoverage { get; set; } = new();

    /// <summary>
    /// Pages with zero cited items and zero tables — a coverage gap worth flagging before
    /// claiming the extraction is complete, regardless of how many fields were found overall.
    /// </summary>
    [JsonPropertyName("pages_with_no_extracted_content")]
    public List<int> PagesWithNoExtractedContent { get; set; } = new();

    [JsonPropertyName("calibrated_envelope")]
    public DocumentEnvelope CalibratedEnvelope { get; set; } = new();
}

/// <summary>
/// Coverage summary for a single physical page of the source document.
/// </summary>
public class PageCoverage
{
    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("is_scanned")]
    public bool IsScanned { get; set; }

    [JsonPropertyName("extracted_item_count")]
    public int ExtractedItemCount { get; set; }

    [JsonPropertyName("table_count")]
    public int TableCount { get; set; }

    /// <summary>
    /// Whether a non-trivial markdown transcript came back for this page — the primary coverage
    /// signal under the markdown-first extraction design, where Items/Tables are expected to be
    /// empty by design and so can no longer be used alone to tell "page covered" from "page missed".
    /// </summary>
    [JsonPropertyName("has_markdown_content")]
    public bool HasMarkdownContent { get; set; }
}

/// <summary>
/// A task queued for human-in-the-loop audit or resolution.
/// </summary>
public class ReviewTask
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = $"REV-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("field_key")]
    public string FieldKey { get; set; } = string.Empty;

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty; // "low_confidence", "unverified_evidence", "unresolved_conflict", "scanned_page_audit"

    [JsonPropertyName("reported_value")]
    public object? ReportedValue { get; set; }

    [JsonPropertyName("reported_confidence")]
    public double ReportedConfidence { get; set; }

    [JsonPropertyName("source_quote")]
    public string SourceQuote { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "open"; // "open", "approved", "rejected", "overridden"

    [JsonPropertyName("reviewer_notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewerNotes { get; set; }

    [JsonPropertyName("resolved_value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ResolvedValue { get; set; }
}

/// <summary>
/// Configurable confidence thresholds and quality policies.
/// </summary>
public class QualityPolicy
{
    /// <summary>
    /// Threshold below which an extraction must be routed to human review. Default: 0.85.
    /// </summary>
    public double ReviewThreshold { get; set; } = 0.85;

    /// <summary>
    /// Penalty applied to confidence when evidence quote cannot be verified on digital text pages. Default: 0.15.
    /// </summary>
    public double UnverifiedPenalty { get; set; } = 0.15;

    /// <summary>
    /// Whether source quotes must be verified against actual PDF text. Default: true.
    /// </summary>
    public bool EnforceSourceVerification { get; set; } = true;

    /// <summary>
    /// Minimum string length for source quotations to be considered meaningful evidence.
    /// </summary>
    public int MinEvidenceLength { get; set; } = 4;
}
