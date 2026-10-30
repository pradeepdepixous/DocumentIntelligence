using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PixousDocIntelligence.Persistence.Entities;

[Table("validation_profiles")]
public class ValidationProfileEntity
{
    [Key]
    [Column("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [Column("tenant_id")]
    public string TenantId { get; set; } = "default-tenant";

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [Column("version")]
    public int Version { get; set; } = 1;

    [Column("requirements_json", TypeName = "jsonb")]
    public string RequirementsJson { get; set; } = "[]";

    [Column("decision_policy_json", TypeName = "jsonb")]
    public string DecisionPolicyJson { get; set; } = "{}";

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("validation_results")]
public class ValidationResultEntity
{
    [Key]
    [Column("validation_id")]
    public string ValidationId { get; set; } = string.Empty;

    [Column("tenant_id")]
    public string TenantId { get; set; } = "default-tenant";

    [Column("profile_id")]
    public string? ProfileId { get; set; }

    [Column("profile_version")]
    public int? ProfileVersion { get; set; }

    [Column("document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [Column("status")]
    public string Status { get; set; } = "manual_review"; // "valid" | "invalid" | "manual_review"

    [Column("is_valid")]
    public bool? IsValid { get; set; }

    [Column("checks_json", TypeName = "jsonb")]
    public string ChecksJson { get; set; } = "[]";

    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Column("validated_at")]
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
}

