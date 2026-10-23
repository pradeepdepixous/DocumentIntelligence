using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Pipeline.Ingestion;

/// <summary>
/// Status of an extraction job lifecycle.
/// </summary>
public enum JobStatus
{
    Created,
    Planned,
    Scheduled,
    Extracting,
    Stitching,
    Completed,
    Failed,
    RequiresReview
}

/// <summary>
/// Metadata and tracking entity for an ingested document and its extraction job.
/// </summary>
public class DocumentJob
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = $"JOB-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = $"DOC-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = "default-tenant";

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("sha256_checksum")]
    public string Sha256Checksum { get; set; } = string.Empty;

    [JsonPropertyName("page_count")]
    public int PageCount { get; set; }

    [JsonPropertyName("status")]
    public JobStatus Status { get; set; } = JobStatus.Created;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("error_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}
