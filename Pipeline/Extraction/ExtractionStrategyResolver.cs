using PixousDocIntelligence.Domain.Planning;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Resolves the appropriate IExtractionStrategy based on the job's ExtractionPlan and caller overrides.
/// </summary>
public static class ExtractionStrategyResolver
{
    private static readonly IExtractionStrategy SchemaStrategy = new SchemaExtractionStrategy();
    private static readonly IExtractionStrategy MarkdownStrategy = new MarkdownExtractionStrategy();

    /// <summary>
    /// Resolves the extraction strategy.
    /// Prefers explicit caller override (e.g. "schema" vs "markdown"); falls back to plan inspection.
    /// </summary>
    public static IExtractionStrategy Resolve(ExtractionPlan? plan, string? requestedMode = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedMode))
        {
            if (requestedMode.Equals("markdown", StringComparison.OrdinalIgnoreCase) ||
                requestedMode.Equals("full_content", StringComparison.OrdinalIgnoreCase))
            {
                return MarkdownStrategy;
            }
            if (requestedMode.Equals("schema", StringComparison.OrdinalIgnoreCase) ||
                requestedMode.Equals("requested_fields", StringComparison.OrdinalIgnoreCase))
            {
                return SchemaStrategy;
            }
        }

        if (plan != null)
        {
            if (plan.ExtractionMode.Equals("full_content", StringComparison.OrdinalIgnoreCase) ||
                plan.ExtractionMode.Equals("markdown", StringComparison.OrdinalIgnoreCase))
            {
                return MarkdownStrategy;
            }

            if (plan.DesiredFields.Count > 0)
            {
                return SchemaStrategy;
            }
        }

        // Default to schema-guided extraction for structured intelligence
        return SchemaStrategy;
    }
}
