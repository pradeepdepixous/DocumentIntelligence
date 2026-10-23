using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Application.Models;

/// <summary>
/// Status of an individual rule evaluation.
/// </summary>
public enum RuleStatus
{
    Pass,
    Fail,
    Partial,
    NotApplicable
}

/// <summary>
/// Definition of a compliance rule or rubric evaluation criterion.
/// </summary>
public class RuleDefinition
{
    private string _ruleId = string.Empty;
    private string _description = string.Empty;
    private bool _isKnockout = false;
    private double _maxMarks = 10.0;

    /// <summary>
    /// Unique identifier for the rule (e.g. 'RULE-01', 'SEC-01', 'FIN-02').
    /// </summary>
    [JsonPropertyName("rule_id")]
    public string RuleId 
    { 
        get => _ruleId; 
        set => _ruleId = value; 
    }

    /// <summary>
    /// Alias for camelCase ruleId.
    /// </summary>
    [JsonPropertyName("ruleId")]
    public string? RuleIdCamelCase 
    { 
        get => _ruleId; 
        set { if (!string.IsNullOrWhiteSpace(value)) _ruleId = value; } 
    }

    /// <summary>
    /// Category or domain (e.g. 'Security &amp; Compliance', 'Performance SLA', 'Financial Terms', 'Legal').
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "General";

    /// <summary>
    /// Optional rule name / title.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Exact requirement description to validate against the document content.
    /// </summary>
    [JsonPropertyName("rule_description")]
    public string RuleDescription 
    { 
        get => !string.IsNullOrWhiteSpace(_description) ? _description : (Name ?? string.Empty); 
        set => _description = value; 
    }

    /// <summary>
    /// Alias for description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? DescriptionAlias 
    { 
        get => _description; 
        set { if (!string.IsNullOrWhiteSpace(value)) _description = value; } 
    }

    /// <summary>
    /// Alias for ruleDescription in camelCase.
    /// </summary>
    [JsonPropertyName("ruleDescription")]
    public string? RuleDescriptionCamelCase 
    { 
        get => _description; 
        set { if (!string.IsNullOrWhiteSpace(value)) _description = value; } 
    }

    /// <summary>
    /// If true, failure of this rule immediately fails the entire document regardless of total score (Dealbreaker / Knockout).
    /// </summary>
    [JsonPropertyName("is_knockout")]
    public bool IsKnockout 
    { 
        get => _isKnockout; 
        set => _isKnockout = value; 
    }

    /// <summary>
    /// Alias for isKnockout.
    /// </summary>
    [JsonPropertyName("isKnockout")]
    public bool? IsKnockoutCamelCase 
    { 
        get => _isKnockout; 
        set { if (value.HasValue) _isKnockout = value.Value; } 
    }

    /// <summary>
    /// Alias for ruleType (e.g. 'Knockout' or 'Rubric').
    /// </summary>
    [JsonPropertyName("ruleType")]
    public string? RuleType 
    { 
        get => _isKnockout ? "Knockout" : "Rubric"; 
        set { if (string.Equals(value, "Knockout", StringComparison.OrdinalIgnoreCase)) _isKnockout = true; } 
    }

    /// <summary>
    /// Maximum marks or points allocated for this rule (e.g. 10.0, 20.0).
    /// </summary>
    [JsonPropertyName("max_marks")]
    public double MaxMarks 
    { 
        get => _maxMarks; 
        set => _maxMarks = value; 
    }

    /// <summary>
    /// Alias for maxMarks.
    /// </summary>
    [JsonPropertyName("maxMarks")]
    public double? MaxMarksCamelCase 
    { 
        get => _maxMarks; 
        set { if (value.HasValue) _maxMarks = value.Value; } 
    }

    /// <summary>
    /// Alias for max_marks / weight.
    /// </summary>
    [JsonPropertyName("weight")]
    public double? WeightAlias 
    { 
        get => _maxMarks; 
        set { if (value.HasValue) _maxMarks = value.Value; } 
    }
}

/// <summary>
/// Request payload containing a checklist of rules to validate against a document.
/// </summary>
public class RuleValidationRequest
{
    private double _threshold = 70.0;
    private double? _totalMarksCap;

    /// <summary>
    /// Total points cap for the evaluation (e.g. 100). The sum of all rules' max_marks must not exceed this cap.
    /// </summary>
    [JsonPropertyName("total_marks_cap")]
    public double? TotalMarksCap 
    { 
        get => _totalMarksCap; 
        set => _totalMarksCap = value; 
    }

    /// <summary>
    /// Alias for totalMarksCap in camelCase.
    /// </summary>
    [JsonPropertyName("totalMarksCap")]
    public double? TotalMarksCapCamelCase 
    { 
        get => _totalMarksCap; 
        set { if (value.HasValue) _totalMarksCap = value; } 
    }

    /// <summary>
    /// Minimum overall percentage (0-100) required for the document to pass evaluative scoring. Default: 70%.
    /// </summary>
    [JsonPropertyName("passing_threshold")]
    public double PassingThreshold 
    { 
        get => _threshold; 
        set => _threshold = value; 
    }

    /// <summary>
    /// Alias for passingThreshold in camelCase.
    /// </summary>
    [JsonPropertyName("passingThreshold")]
    public double? PassingThresholdCamelCase 
    { 
        get => _threshold; 
        set { if (value.HasValue) _threshold = value.Value; } 
    }

    /// <summary>
    /// LLM Provider to evaluate rules ('Gemini' or 'OpenAI'). Defaults to server configuration if null.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// Specific model identifier (e.g. 'gpt-4o-mini', 'gpt-4o', 'gemini-flash-latest').
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Optional workflow-specific API key. Overrides the server environment key for this evaluation.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// List of rules and criteria to validate.
    /// </summary>
    [JsonPropertyName("rules")]
    public List<RuleDefinition> Rules { get; set; } = new();
}

/// <summary>
/// Individual finding and marks breakdown for a specific rule.
/// </summary>
public class RuleEvaluationResult
{
    private string _evidenceQuote = string.Empty;

    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public RuleStatus Status { get; set; } = RuleStatus.Pass;

    [JsonPropertyName("is_knockout")]
    public bool IsKnockout { get; set; }

    [JsonPropertyName("marks_awarded")]
    public double MarksAwarded { get; set; }

    [JsonPropertyName("max_marks")]
    public double MaxMarks { get; set; }

    [JsonPropertyName("finding")]
    public string Finding { get; set; } = string.Empty;

    /// <summary>
    /// Concrete reason explaining why the rule passed or failed.
    /// </summary>
    [JsonPropertyName("decision_reason")]
    public string DecisionReason { get; set; } = string.Empty;

    /// <summary>
    /// Detailed breakdown explaining why these specific marks were awarded or deducted.
    /// </summary>
    [JsonPropertyName("marks_reason")]
    public string MarksReason { get; set; } = string.Empty;

    [JsonPropertyName("page_citations")]
    public List<int> PageCitations { get; set; } = new();

    /// <summary>
    /// Exact quote or excerpt from the document providing proof.
    /// </summary>
    [JsonPropertyName("evidence_quote")]
    public string EvidenceQuote 
    { 
        get => _evidenceQuote; 
        set => _evidenceQuote = value; 
    }

    /// <summary>
    /// Alias for evidence / evidence_quote.
    /// </summary>
    [JsonPropertyName("evidence")]
    public string? EvidenceAlias 
    { 
        get => _evidenceQuote; 
        set { if (!string.IsNullOrWhiteSpace(value)) _evidenceQuote = value; } 
    }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.95;

    [JsonPropertyName("data_quality_warning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataQualityWarning { get; set; }
}

/// <summary>
/// Consolidated compliance scorecard and audit report.
/// </summary>
public class RuleValidationResponse
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("overall_verdict")]
    public string OverallVerdict { get; set; } = "PASS"; // "PASS", "FAIL", "NEEDS_REVIEW"

    [JsonPropertyName("total_marks_cap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TotalMarksCap { get; set; }

    [JsonPropertyName("total_score_achieved")]
    public double TotalScoreAchieved { get; set; }

    /// <summary>
    /// Alias for total_score_achieved.
    /// </summary>
    [JsonPropertyName("marks_awarded")]
    public double MarksAwarded => TotalScoreAchieved;

    [JsonPropertyName("total_max_score")]
    public double TotalMaxScore { get; set; }

    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }

    [JsonPropertyName("passing_threshold")]
    public double PassingThreshold { get; set; }

    [JsonPropertyName("knockout_passed")]
    public bool KnockoutPassed { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("evaluated_at")]
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("rule_results")]
    public List<RuleEvaluationResult> RuleResults { get; set; } = new();
}

