using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Strategy pattern contract for Phase 4 batch extraction.
/// Separates schema-guided field extraction from markdown-first transcription.
/// </summary>
public interface IExtractionStrategy
{
    /// <summary>
    /// Identifier for this extraction mode (e.g. "schema", "markdown").
    /// </summary>
    string ModeName { get; }

    /// <summary>
    /// Constructs the system instruction and user prompt for this batch and extraction plan.
    /// </summary>
    (string SystemInstruction, string UserPrompt) BuildPrompts(BatchDescriptor batch, ExtractionPlan plan);

    /// <summary>
    /// Parses the raw JSON response returned by the LLM into a populated DocumentEnvelope.
    /// </summary>
    DocumentEnvelope ParseEnvelope(string rawJson, BatchDescriptor batch, ExtractionPlan plan);
}
