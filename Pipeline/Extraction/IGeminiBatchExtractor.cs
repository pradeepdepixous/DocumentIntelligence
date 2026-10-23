using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Service responsible for Phase 4: Executing batch extraction on an individual page batch
/// using the job's versioned ExtractionPlan.
/// </summary>
public interface IGeminiBatchExtractor : IBatchExtractor
{
}
