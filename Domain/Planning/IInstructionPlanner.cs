namespace PixousDocIntelligence.Domain.Planning;

/// <summary>
/// Service responsible for translating natural language user extraction goals
/// into a structured, frozen, reproducible ExtractionPlan using Gemini 2.5 Flash.
/// </summary>
public interface IInstructionPlanner
{
    /// <summary>
    /// Generates a validated ExtractionPlan from a user's extraction request and optional document context.
    /// </summary>
    /// <param name="userInstruction">Natural language extraction instructions from user.</param>
    /// <param name="documentTypeHint">Optional document type hint (e.g. "Invoice", "Brochure", "Contract").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A validated, frozen ExtractionPlan.</returns>
    Task<ExtractionPlan> GeneratePlanAsync(
        string userInstruction, 
        string? documentTypeHint = null, 
        CancellationToken cancellationToken = default);
}
