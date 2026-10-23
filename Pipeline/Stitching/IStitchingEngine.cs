using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;

namespace PixousDocIntelligence.Pipeline.Stitching;

public class StitchingResult
{
    public DocumentEnvelope Envelope { get; set; } = new();
    public ReconciliationLedger Ledger { get; set; } = new();
}

/// <summary>
/// Service responsible for Phase 5: Confidence-aware stitching, deduplication across
/// overlapping page boundaries, and conflict preservation.
/// </summary>
public interface IStitchingEngine
{
    /// <summary>
    /// Reconciles batch envelopes into a unified, consolidated DocumentEnvelope.
    /// Preserves conflicting observations in the conflict ledger without silent loss.
    /// </summary>
    DocumentEnvelope StitchBatches(
        IEnumerable<DocumentEnvelope> batchEnvelopes, 
        ExtractionPlan plan);

    /// <summary>
    /// Reconciles batch envelopes and returns both the stitched envelope and the complete explainable audit ledger.
    /// </summary>
    StitchingResult StitchBatchesDetailed(
        IEnumerable<DocumentEnvelope> batchEnvelopes, 
        ExtractionPlan plan);
}
