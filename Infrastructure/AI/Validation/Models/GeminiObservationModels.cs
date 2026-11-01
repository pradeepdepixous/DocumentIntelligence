using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Infrastructure.AI.Validation.Models;

/// <summary>
/// Raw observational payload returned by Gemini.
/// Gemini ONLY reports observed facts, never the final business validity verdict.
/// </summary>
public class GeminiDocumentObservation
{
    [JsonPropertyName("detectedDocumentType")]
    public string DetectedDocumentType { get; set; } = string.Empty;

    [JsonPropertyName("documentTypeConfidence")]
    public double DocumentTypeConfidence { get; set; }

    [JsonPropertyName("observations")]
    public List<GeminiRequirementObservation> Observations { get; set; } = new();
}

public class GeminiRequirementObservation
{
    [JsonPropertyName("requirementId")]
    public string RequirementId { get; set; } = string.Empty;

    /// <summary>
    /// "present" if clearly observed, "missing" if absent from the document, "ambiguous" if blurry/partial/unclear.
    /// </summary>
    [JsonPropertyName("observationState")]
    public string ObservationState { get; set; } = "ambiguous";

    /// <summary>
    /// Exact quote or visual finding from the document supporting the observation.
    /// </summary>
    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;

    /// <summary>
    /// Estimated confidence score (0.0 to 1.0) for this observation.
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

