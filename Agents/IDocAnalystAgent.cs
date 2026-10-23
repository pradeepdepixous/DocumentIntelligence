using PixousDocIntelligence.Models;

namespace PixousDocIntelligence.Agents;

/// <summary>
/// Route B Agent interface for analyzing and interacting with schema-independent structured documents.
/// </summary>
public interface IDocAnalystAgent
{
    /// <summary>
    /// Gets whether a document is currently active in the agent's working context.
    /// </summary>
    bool HasDocumentLoaded { get; }

    /// <summary>
    /// Currently loaded structured document result.
    /// </summary>
    DocumentAnalysisResult? CurrentDocument { get; }

    /// <summary>
    /// Loads a structured document into the agent's in-memory working context.
    /// </summary>
    void LoadDocument(DocumentAnalysisResult document, string? rawJson = null, string? rawDocumentText = null);

    /// <summary>
    /// Tool 1: Summarizer Tool. Synthesizes an executive, contextual summary with page distribution.
    /// </summary>
    /// <param name="focusArea">Optional specific angle (e.g. "pricing", "safety", "services", "terms").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> SummarizeAsync(string? focusArea = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tool 2: Q&amp;A Tool. Answers user questions with strict grounding, confidence tags, and page attribution.
    /// </summary>
    /// <param name="question">User question against the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<QnAResult> AskQuestionAsync(string question, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tool 3: Compliance &amp; Rule Validation Tool. Validates a checklist of rules against the document,
    /// enforcing critical knockout rules (Pass/Fail) and weighted rubric scoring (Marks).
    /// </summary>
    /// <param name="request">Checklist of rules and passing thresholds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PixousDocIntelligence.Application.Models.RuleValidationResponse> ValidateRulesAsync(
        PixousDocIntelligence.Application.Models.RuleValidationRequest request, 
        CancellationToken cancellationToken = default);
}
