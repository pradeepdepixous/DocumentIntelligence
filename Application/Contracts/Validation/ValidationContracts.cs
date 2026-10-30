using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using PixousDocIntelligence.Domain.Validation;

namespace PixousDocIntelligence.Application.Contracts.Validation;

/// <summary>
/// Inline configuration provided directly in the validation request payload.
/// </summary>
public class InlineValidationConfiguration
{
    [JsonPropertyName("documentType")]
    public string ExpectedDocumentType { get; set; } = string.Empty;

    [JsonPropertyName("requirements")]
    public List<ValidationRequirement> Requirements { get; set; } = new();

    [JsonPropertyName("decisionPolicy")]
    public DecisionPolicy? DecisionPolicy { get; set; }
}

/// <summary>
/// Multipart form-data request payload for uploading and validating a document.
/// </summary>
public class ValidateDocumentUploadRequest
{
    /// <summary>
    /// The uploaded document file (PDF, PNG, JPEG, etc.).
    /// </summary>
    public IFormFile? File { get; set; }

    /// <summary>
    /// Inline validation configuration as a JSON string (used for dynamic/inline checks).
    /// </summary>
    public string? RequirementsJson { get; set; }

    /// <summary>
    /// Optional saved validation profile ID (used for profile-driven checks in Phase 2).
    /// </summary>
    public string? ProfileId { get; set; }
}

/// <summary>
/// Result of evaluating an individual requirement against document evidence.
/// </summary>
public class RequirementCheckResult
{
    [JsonPropertyName("requirementId")]
    public string RequirementId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "uncertain"; // "passed" | "failed" | "uncertain"

    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

/// <summary>
/// Structured validation response returned to the consuming application.
/// </summary>
public class ValidationResponse
{
    [JsonPropertyName("validationId")]
    public string ValidationId { get; set; } = $"val_{Guid.NewGuid():N}";

    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "manual_review"; // "valid" | "invalid" | "manual_review"

    [JsonPropertyName("isValid")]
    public bool? IsValid { get; set; }

    [JsonPropertyName("checks")]
    public List<RequirementCheckResult> Checks { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("validatedAtUtc")]
    public DateTime ValidatedAtUtc { get; set; } = DateTime.UtcNow;
}

