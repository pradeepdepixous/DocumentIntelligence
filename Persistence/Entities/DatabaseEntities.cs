using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PixousDocIntelligence.Persistence.Entities;

[Table("documents")]
public class DocumentEntity
{
    [Key]
    [Column("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [Column("tenant_id")]
    public string TenantId { get; set; } = "default-tenant";

    [Column("file_name")]
    public string FileName { get; set; } = string.Empty;

    [Column("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [Column("file_size_bytes")]
    public long FileSizeBytes { get; set; }

    [Column("sha256_checksum")]
    public string Sha256Checksum { get; set; } = string.Empty;

    [Column("page_count")]
    public int PageCount { get; set; }

    [Column("status")]
    public string Status { get; set; } = "ingested";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("extraction_jobs")]
public class ExtractionJobEntity
{
    [Key]
    [Column("job_id")]
    public string JobId { get; set; } = string.Empty;

    [Column("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [Column("tenant_id")]
    public string TenantId { get; set; } = "default-tenant";

    [Column("status")]
    public string Status { get; set; } = "created";

    [Column("user_instruction")]
    public string? UserInstruction { get; set; }

    [Column("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [Column("extraction_plan", TypeName = "jsonb")]
    public string ExtractionPlanJson { get; set; } = "{}";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("stitched_records")]
public class StitchedRecordEntity
{
    [Key]
    [Column("record_id")]
    public string RecordId { get; set; } = Guid.NewGuid().ToString("N");

    [Column("job_id")]
    public string JobId { get; set; } = string.Empty;

    [Column("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [Column("consolidated_payload", TypeName = "jsonb")]
    public string ConsolidatedPayloadJson { get; set; } = "{}";

    [Column("unresolved_conflicts_count")]
    public int UnresolvedConflictsCount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("review_tasks")]
public class ReviewTaskEntity
{
    [Key]
    [Column("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [Column("job_id")]
    public string JobId { get; set; } = string.Empty;

    [Column("field_key")]
    public string FieldKey { get; set; } = string.Empty;

    [Column("page_number")]
    public int PageNumber { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("reported_value", TypeName = "jsonb")]
    public string ReportedValueJson { get; set; } = "{}";

    [Column("reported_confidence")]
    public decimal ReportedConfidence { get; set; }

    [Column("source_quote")]
    public string? SourceQuote { get; set; }

    [Column("status")]
    public string Status { get; set; } = "open";

    [Column("reviewer_notes")]
    public string? ReviewerNotes { get; set; }

    [Column("resolved_value", TypeName = "jsonb")]
    public string? ResolvedValueJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }
}

[Table("batch_manifests")]
public class BatchManifestEntity
{
    [Key]
    [Column("job_id")]
    public string JobId { get; set; } = string.Empty;

    [Column("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [Column("total_pages")]
    public int TotalPages { get; set; }

    [Column("batch_size")]
    public int BatchSize { get; set; }

    [Column("overlap_size")]
    public int OverlapSize { get; set; }

    [Column("manifest_payload", TypeName = "jsonb")]
    public string ManifestPayloadJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("extraction_batches")]
public class ExtractionBatchEntity
{
    [Column("job_id")]
    public string JobId { get; set; } = string.Empty;

    [Column("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [Column("start_page")]
    public int StartPage { get; set; }

    [Column("end_page")]
    public int EndPage { get; set; }

    [Column("overlap_start_page")]
    public int? OverlapStartPage { get; set; }

    [Column("overlap_end_page")]
    public int? OverlapEndPage { get; set; }

    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Column("retry_count")]
    public int RetryCount { get; set; }

    [Column("extracted_item_count")]
    public int ExtractedItemCount { get; set; }

    [Column("processing_duration_ms")]
    public long ProcessingDurationMs { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("reconciliation_ledgers")]
public class ReconciliationLedgerEntity
{
    [Key]
    [Column("job_id")]
    public string JobId { get; set; } = string.Empty;

    [Column("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [Column("ledger_payload", TypeName = "jsonb")]
    public string LedgerPayloadJson { get; set; } = "{}";

    [Column("conflict_count")]
    public int ConflictCount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A customer portal account, created via Google sign-in and/or email+password registration.
/// The same account can have both: signing in with Google using an email that already has a
/// password-based account links GoogleSubject onto it rather than creating a duplicate.
/// Distinct from a ClientCredential (API key): one account may own several keys/apps.
/// </summary>
[Table("customer_accounts")]
public class CustomerAccountEntity
{
    [Key]
    [Column("account_id")]
    public string AccountId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Null for accounts that only ever registered with email+password.</summary>
    [Column("google_subject")]
    public string? GoogleSubject { get; set; }

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("picture_url")]
    public string? PictureUrl { get; set; }

    /// <summary>Null for accounts that only ever signed in with Google.</summary>
    [Column("password_hash")]
    public string? PasswordHash { get; set; }

    [Column("failed_login_attempts")]
    public int FailedLoginAttempts { get; set; }

    [Column("locked_out_until")]
    public DateTime? LockedOutUntil { get; set; }

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Page-processing credit pool for this account, shared by every self-serve API key it owns
    /// AND by testing directly in the Playground via the portal session — one limit, spent either way.
    /// </summary>
    [Column("credit_limit")]
    public int CreditLimit { get; set; }

    [Column("credits_used")]
    public int CreditsUsed { get; set; }

    [Column("billing_cycle")]
    public string BillingCycle { get; set; } = DateTime.UtcNow.ToString("yyyy-MM");
}

