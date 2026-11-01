using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using PixousDocIntelligence.Domain.Planning;

namespace PixousDocIntelligence.Application.Models;

/// <summary>
/// Multipart form request for batch uploading up to 300 procurement/bidding documents.
/// </summary>
public class CreateCampaignBatchRequest
{
    /// <summary>
    /// Optional human-readable name for the bidding campaign (e.g., '2026 Enterprise Cloud Infrastructure RFP').
    /// </summary>
    public string? CampaignName { get; set; }

    /// <summary>
    /// The collection of bid documents (PDF files, up to 300).
    /// </summary>
    [Required]
    public List<IFormFile> Files { get; set; } = new();

    /// <summary>
    /// Shared manual JSON schema applied to all bids in this campaign.
    /// Can be OpenAPI JSON Schema, Pixous field array, or key-value dictionary.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Optional manual JSON schema uploaded as a .json file.
    /// </summary>
    public IFormFile? SchemaFile { get; set; }

    /// <summary>
    /// Optional instructions (used if no schema is provided).
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Engine Tier: 'fast' (Gemini Flash-Lite, default) or 'premium' (OpenAI GPT-4o).
    /// </summary>
    public string? Tier { get; set; } = "fast";

    /// <summary>
    /// Extraction mode: 'schema' (default) or 'markdown'.
    /// </summary>
    public string? Mode { get; set; } = "schema";
}

/// <summary>
/// Instant response returned with HTTP 202 Accepted when a batch campaign is accepted.
/// </summary>
public class CampaignAcceptedResponse
{
    [JsonPropertyName("campaign_id")]
    public string CampaignId { get; set; } = string.Empty;

    [JsonPropertyName("campaign_name")]
    public string CampaignName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Processing";

    [JsonPropertyName("total_documents")]
    public int TotalDocuments { get; set; }

    [JsonPropertyName("queued_at")]
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("status_url")]
    public string StatusUrl { get; set; } = string.Empty;

    [JsonPropertyName("bids_url")]
    public string BidsUrl { get; set; } = string.Empty;

    [JsonPropertyName("storage_directory")]
    public string StorageDirectory { get; set; } = string.Empty;
}

/// <summary>
/// Aggregated real-time campaign execution status.
/// </summary>
public class CampaignStatusResponse
{
    [JsonPropertyName("campaign_id")]
    public string CampaignId { get; set; } = string.Empty;

    [JsonPropertyName("campaign_name")]
    public string CampaignName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Processing"; // 'Processing', 'Completed', 'PartialFailures'

    [JsonPropertyName("total_bids")]
    public int TotalBids { get; set; }

    [JsonPropertyName("completed_count")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; set; }

    [JsonPropertyName("progress_percentage")]
    public double ProgressPercentage => TotalBids > 0 ? Math.Round((double)CompletedCount / TotalBids * 100, 1) : 0;

    [JsonPropertyName("queued_at")]
    public DateTime QueuedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("bids")]
    public List<BidItemSummary> Bids { get; set; } = new();
}

public class BidItemSummary
{
    [JsonPropertyName("bid_id")]
    public string BidId { get; set; } = string.Empty;

    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("organization_name")]
    public string OrganizationName { get; set; } = "Unknown Organization";

    [JsonPropertyName("source_file_name")]
    public string SourceFileName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Queued"; // 'Queued', 'Processing', 'Succeeded', 'NeedsReview', 'Failed'

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("json_path")]
    public string JsonPath { get; set; } = string.Empty;
}

/// <summary>
/// Clean, isolated, self-contained JSON record stored separately on disk for every single bid.
/// </summary>
public class IsolatedBidRecord
{
    [JsonPropertyName("bid_id")]
    public string BidId { get; set; } = string.Empty;

    [JsonPropertyName("campaign_id")]
    public string CampaignId { get; set; } = string.Empty;

    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("organization_name")]
    public string OrganizationName { get; set; } = string.Empty;

    [JsonPropertyName("source_file_name")]
    public string SourceFileName { get; set; } = string.Empty;

    [JsonPropertyName("processed_at")]
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("execution_duration_ms")]
    public long ExecutionDurationMs { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Succeeded";

    [JsonPropertyName("plan_id")]
    public string PlanId { get; set; } = string.Empty;

    [JsonPropertyName("extracted_fields")]
    public Dictionary<string, ExtractedFieldDetail> ExtractedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("tables")]
    public List<ExtractedTableDetail> Tables { get; set; } = new();

    [JsonPropertyName("verification_summary")]
    public VerificationSummaryDetail VerificationSummary { get; set; } = new();
}

public class ExtractedFieldDetail
{
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; set; } = "verified";

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("source_quote")]
    public string? SourceQuote { get; set; }
}

public class ExtractedTableDetail
{
    [JsonPropertyName("headers")]
    public List<string> Headers { get; set; } = new();

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("rows")]
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
}

public class VerificationSummaryDetail
{
    [JsonPropertyName("total_fields")]
    public int TotalFields { get; set; }

    [JsonPropertyName("verified_fields")]
    public int VerifiedFields { get; set; }

    [JsonPropertyName("unverified_fields")]
    public int UnverifiedFields { get; set; }

    [JsonPropertyName("review_tasks_count")]
    public int ReviewTasksCount { get; set; }
}
