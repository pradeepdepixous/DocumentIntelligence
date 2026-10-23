using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Provider-agnostic batch extraction interface for Phase 4.
/// Extracts structured fields and tables from document slices.
/// </summary>
public interface IBatchExtractor
{
    /// <summary>
    /// Executes extraction on a single batch payload, returning a populated DocumentEnvelope.
    /// </summary>
    Task<DocumentEnvelope> ExtractBatchAsync(
        byte[] batchPdfBytes, 
        BatchDescriptor batchDescriptor, 
        ExtractionPlan plan, 
        CancellationToken cancellationToken = default);
}
