using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Domain.Validation;

/// <summary>
/// Status of an individual requirement check.
/// </summary>
public enum CheckStatus
{
    Passed,
    Failed,
    Uncertain
}

/// <summary>
/// Final deterministic validation decision for the entire document.
/// </summary>
public enum ValidationStatus
{
    Valid,
    Invalid,
    ManualReview
}

/// <summary>
/// Policy governing how uncertain or failing checks translate into the final decision.
/// </summary>
public class DecisionPolicy
{
    /// <summary>
    /// Action to take when a mandatory requirement is uncertain:
    /// "manual_review" (default), "invalid", or "valid".
    /// </summary>
    [JsonPropertyName("onUncertain")]
    public string OnUncertain { get; set; } = "manual_review";

    /// <summary>
    /// Calibrated minimum confidence threshold (0.0 - 1.0) required to consider an observation Passed.
    /// Default is 0.80.
    /// </summary>
    [JsonPropertyName("minConfidenceThreshold")]
    public double MinConfidenceThreshold { get; set; } = 0.80;

    /// <summary>
    /// If true, any mandatory requirement failure immediately marks the entire document Invalid.
    /// </summary>
    [JsonPropertyName("requireAllMandatory")]
    public bool RequireAllMandatory { get; set; } = true;
}

