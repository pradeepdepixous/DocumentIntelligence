using System.Text;
using System.Text.Json;
using PixousDocIntelligence.Services.LLM;

namespace PixousDocIntelligence.Domain.Planning;

/// <summary>
/// Gemini-powered instruction planner that translates user instructions into an immutable ExtractionPlan.
/// </summary>
public class InstructionPlanner : IInstructionPlanner
{
    private readonly ILLMClient _llmClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public InstructionPlanner(ILLMClient llmClient)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
    }

    public InstructionPlanner(
        string apiKey, 
        string modelName = "gemini-flash-lite-latest", 
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        }

        _llmClient = new Services.LLM.GeminiLLMClient(apiKey, modelName, httpClient);
    }

    /// <inheritdoc />
    public async Task<ExtractionPlan> GeneratePlanAsync(
        string userInstruction, 
        string? documentTypeHint = null, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInstruction))
        {
            userInstruction = "Extract all critical business entities, dates, parties, monetary figures, terms, and structured tables.";
        }

        string systemInstruction = 
            "You are an expert Document Intelligence Architect and Schema Planner.\n" +
            "Your objective is to translate user natural language instructions into a rigorous, unambiguous ExtractionPlan.\n\n" +
            "PLAN REQUIREMENTS:\n" +
            "1. 'target_document_type': Infer the document category (e.g. 'Commercial Lease Agreement', 'Financial Statement', 'Product Catalog', 'Technical Brochure', 'Tax Invoice').\n" +
            "2. 'extraction_mode': Choose 'requested_fields' (specific targeted attributes), 'full_content' (comprehensive capture), or 'section_focused'.\n" +
            "3. 'desired_fields': Break down the request into discrete fields. For each field provide:\n" +
            "   - 'field_key': standardized snake_case identifier (e.g. 'security_deposit', 'notice_period')\n" +
            "   - 'field_label': clean human-readable title (e.g. 'Security Deposit')\n" +
            "   - 'value_type': 'number', 'string', 'date', 'currency', 'boolean', or 'array'\n" +
            "   - 'description': precise extraction guidance\n" +
            "   - 'aliases': alternative names that might appear in documents\n" +
            "   - 'is_mandatory': true if explicitly requested\n" +
            "4. 'preserve_tables': true if tabular data or line items are expected.\n" +
            "5. 'normalization_rules': specify date formats (e.g. 'YYYY-MM-DD'), currency standards (e.g. 'ISO-4217'), and deduplication strategy.\n" +
            "6. Output MUST be strictly valid JSON matching the ExtractionPlan schema.";

        string userPrompt = 
            $"USER EXTRACTION REQUEST: {userInstruction}\n" +
            $"DOCUMENT TYPE HINT: {documentTypeHint ?? "Auto-detect from instructions"}\n\n" +
            "Generate the formal ExtractionPlan JSON.";

        string responseJson = await _llmClient.GenerateContentAsync(systemInstruction, userPrompt, requireJson: true, cancellationToken);
        string planJson = CleanJsonFences(responseJson);

        ExtractionPlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<ExtractionPlan>(planJson, JsonOptions)
                   ?? throw new JsonException("Deserialized ExtractionPlan was null.");
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Failed to deserialize generated ExtractionPlan. Raw: {planJson}", ex);
        }

        // Post-validation and freezing
        ValidateAndFreezePlan(plan, userInstruction);

        return plan;
    }

    private static string CleanJsonFences(string input)
    {
        string trimmed = input.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed[3..];
        }
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }
        return trimmed.Trim();
    }

    private static void ValidateAndFreezePlan(ExtractionPlan plan, string userInstruction)
    {
        plan.UserInstruction = userInstruction;
        if (string.IsNullOrWhiteSpace(plan.PlanId))
        {
            plan.PlanId = $"PLAN-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        }
        if (plan.SchemaVersion <= 0)
        {
            plan.SchemaVersion = 1;
        }

        // Ensure all field keys are clean snake_case
        foreach (var field in plan.DesiredFields)
        {
            if (string.IsNullOrWhiteSpace(field.FieldKey))
            {
                field.FieldKey = field.FieldLabel.ToLowerInvariant().Replace(" ", "_");
            }
            else
            {
                field.FieldKey = field.FieldKey.ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            }
        }
    }
}
