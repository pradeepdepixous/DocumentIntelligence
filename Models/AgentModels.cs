namespace PixousDocIntelligence.Models;

/// <summary>
/// Structured result returned by the Q&amp;A Agent Tool.
/// </summary>
public class QnAResult
{
    /// <summary>
    /// The comprehensive, grounded response from the agent.
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Explicit document citations supporting this answer.
    /// </summary>
    public List<FieldCitation> Citations { get; set; } = new();

    /// <summary>
    /// Any warnings regarding low-confidence extractions (&lt; 0.90) or partial information.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Indicates whether the answer was substantiated by the document data.
    /// </summary>
    public bool IsFoundInDocument { get; set; } = true;
}

/// <summary>
/// Source attribution for a specific fact cited in an answer.
/// </summary>
public class FieldCitation
{
    public string FieldName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public double Confidence { get; set; }
    public string? Evidence { get; set; }
}
