using System.Text.Json.Serialization;
using PixousDocIntelligence.Domain.Contracts;

namespace PixousDocIntelligence.Domain.Conflicts;

public enum ReconciliationOutcome
{
    SingleObservation,
    OverlapMerged,
    ConflictPreserved,
    HumanResolved,
    RuleResolved
}

/// <summary>
/// Individual explainable audit entry documenting how a specific field was reconciled.
/// </summary>
public class FieldReconciliationDecision
{
    [JsonPropertyName("field_key")]
    public string FieldKey { get; set; } = string.Empty;

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("outcome")]
    public ReconciliationOutcome Outcome { get; set; }

    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = string.Empty;

    [JsonPropertyName("selected_value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? SelectedValue { get; set; }

    [JsonPropertyName("selected_confidence")]
    public double SelectedConfidence { get; set; }

    [JsonPropertyName("selected_evidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedEvidence { get; set; }

    [JsonPropertyName("source_batch_ids")]
    public List<string> SourceBatchIds { get; set; } = new();

    [JsonPropertyName("competing_candidates")]
    public List<CandidateObservation> CompetingCandidates { get; set; } = new();
}

/// <summary>
/// Complete audit ledger capturing all field reconciliation decisions for a document processing run.
/// </summary>
public class ReconciliationLedger
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("total_decisions")]
    public int TotalDecisions => Decisions.Count;

    [JsonPropertyName("merged_count")]
    public int MergedCount => Decisions.Count(d => d.Outcome == ReconciliationOutcome.OverlapMerged);

    [JsonPropertyName("conflict_count")]
    public int ConflictCount => Decisions.Count(d => d.Outcome == ReconciliationOutcome.ConflictPreserved);

    [JsonPropertyName("decisions")]
    public List<FieldReconciliationDecision> Decisions { get; set; } = new();
}
