using System.Text.Json.Serialization;
using PixousDocIntelligence.Domain.Validation;

namespace PixousDocIntelligence.Application.Contracts.Validation;

public class CreateValidationProfileRequest
{
    /// <summary>
    /// Unique profile ID chosen by client (e.g. 'profile_income_cert') or auto-generated if omitted.
    /// </summary>
    [JsonPropertyName("profileId")]
    public string? ProfileId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("requirements")]
    public List<ValidationRequirement> Requirements { get; set; } = new();

    [JsonPropertyName("decisionPolicy")]
    public DecisionPolicy? DecisionPolicy { get; set; }
}

public class UpdateValidationProfileRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("requirements")]
    public List<ValidationRequirement>? Requirements { get; set; }

    [JsonPropertyName("decisionPolicy")]
    public DecisionPolicy? DecisionPolicy { get; set; }

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; set; }
}

public class ValidationProfileDto
{
    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = "default-tenant";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("requirements")]
    public List<ValidationRequirement> Requirements { get; set; } = new();

    [JsonPropertyName("decisionPolicy")]
    public DecisionPolicy DecisionPolicy { get; set; } = new();

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}

