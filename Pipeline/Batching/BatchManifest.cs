using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Pipeline.Batching;

public enum BatchStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    NeedsReview
}

/// <summary>
/// Descriptor representing an individual batch of pages with explicit page-range,
/// overlap tracking, source reference metadata, and execution state.
/// </summary>
public class BatchDescriptor
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("source_document_checksum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceDocumentChecksum { get; set; }

    [JsonPropertyName("start_page")]
    public int StartPage { get; set; }

    [JsonPropertyName("end_page")]
    public int EndPage { get; set; }

    [JsonPropertyName("page_range_label")]
    public string PageRangeLabel => $"Pages {StartPage}..{EndPage}";

    /// <summary>
    /// Beginning of the overlap region shared with the preceding batch.
    /// </summary>
    [JsonPropertyName("overlap_start_page")]
    public int? OverlapStartPage { get; set; }

    /// <summary>
    /// End of the overlap region shared with the subsequent batch.
    /// </summary>
    [JsonPropertyName("overlap_end_page")]
    public int? OverlapEndPage { get; set; }

    [JsonPropertyName("page_count")]
    public int PageCount => (EndPage - StartPage) + 1;

    [JsonPropertyName("status")]
    public BatchStatus Status { get; set; } = BatchStatus.Pending;

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [JsonPropertyName("extracted_item_count")]
    public int ExtractedItemCount { get; set; }

    [JsonPropertyName("processing_duration_ms")]
    public long ProcessingDurationMs { get; set; }

    [JsonPropertyName("execution_timestamp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ExecutionTimestamp { get; set; }

    [JsonPropertyName("error_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Complete schedule manifest for a document broken into overlapping batches.
/// </summary>
public class BatchManifest
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("source_document_checksum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceDocumentChecksum { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    /// <summary>
    /// The MAXIMUM page span of any single batch, not a fixed/exact size. A document with
    /// TotalPages &lt;= BatchSize is scheduled as a single batch covering all pages; only documents
    /// larger than BatchSize are actually split into multiple overlapping batches.
    /// </summary>
    [JsonPropertyName("batch_size")]
    public int BatchSize { get; set; } = 15;

    [JsonPropertyName("overlap_size")]
    public int OverlapSize { get; set; } = 3;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("batches")]
    public List<BatchDescriptor> Batches { get; set; } = new();
}
