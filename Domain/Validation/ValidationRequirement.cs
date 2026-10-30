using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Domain.Validation;

/// <summary>
/// Broad classification of what kind of validation check this requirement performs.
/// </summary>
public enum RequirementType
{
    /// <summary>Confirms whether the document itself belongs to the expected document class (e.g. "Must be an income certificate").</summary>
    DocumentClassification,

    /// <summary>Confirms the presence or visibility of a specific seal, signature, header, or section.</summary>
    ContentPresence,

    /// <summary>Confirms whether a specific extracted value matches an expected pattern, value, or format.</summary>
    FieldMatch,

    /// <summary>Confirms visual legibility, absence of severe cropping, blur, or obstruction.</summary>
    VisualLegibility
}

/// <summary>
/// A single validation requirement supplied by the consuming application or loaded from a validation profile.
/// </summary>
public class ValidationRequirement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RequirementType Type { get; set; } = RequirementType.ContentPresence;

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    /// <summary>
    /// Optional expected value or keyword pattern when a strict value match is desired.
    /// </summary>
    [JsonPropertyName("expectedValue")]
    public string? ExpectedValue { get; set; }
}

