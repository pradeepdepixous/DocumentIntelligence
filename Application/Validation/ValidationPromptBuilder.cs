using System.Text;
using PixousDocIntelligence.Domain.Validation;

namespace PixousDocIntelligence.Application.Validation;

/// <summary>
/// Constructs generic prompts and output schemas for Gemini based on incoming requirements.
/// Has zero hardcoded document types.
/// </summary>
public class ValidationPromptBuilder
{
    public (string SystemInstruction, string UserPrompt) Build(
        string expectedDocumentType,
        IReadOnlyList<ValidationRequirement> requirements)
    {
        var systemSb = new StringBuilder();
        systemSb.AppendLine("You are an expert Document Inspection and Evidence Extraction Engine.");
        systemSb.AppendLine("Your task is to objectively inspect the uploaded document against the specified validation requirements.");
        systemSb.AppendLine();
        systemSb.AppendLine("GROUNDING & OBSERVATION GUIDELINES:");
        systemSb.AppendLine("1. Ground every observation strictly in the visible document content (text, seals, stamps, emblems, signatures, headers).");
        systemSb.AppendLine("2. Do NOT invent, assume, or extrapolate facts. If an item is absent, mark observationState as 'missing'.");
        systemSb.AppendLine("3. If text or an emblem is partially obscured, low-resolution, cut off, or unclear, mark observationState as 'ambiguous'.");
        systemSb.AppendLine("4. If an item is clearly visible and identifiable, mark observationState as 'present'.");
        systemSb.AppendLine("5. For each requirement, quote the exact textual evidence or describe the visible graphic evidence (e.g., 'Emblem visible in header').");
        systemSb.AppendLine("6. You must return an observation for EVERY requirement ID requested.");
        systemSb.AppendLine("7. Provide a realistic confidence score (0.0 to 1.0) reflecting legibility and certainty.");
        systemSb.AppendLine("8. IMPORTANT: You only report observations and evidence. You do NOT make approval decisions.");

        var userSb = new StringBuilder();
        userSb.AppendLine($"EXPECTED DOCUMENT TYPE: {expectedDocumentType}");
        userSb.AppendLine();
        userSb.AppendLine("VALIDATION REQUIREMENTS TO INSPECT:");
        for (int i = 0; i < requirements.Count; i++)
        {
            var req = requirements[i];
            userSb.Append($"{i + 1}. [ID: \"{req.Id}\"] - {req.Description}");
            if (!string.IsNullOrWhiteSpace(req.ExpectedValue))
            {
                userSb.Append($" (Expected Value/Pattern: \"{req.ExpectedValue}\")");
            }
            userSb.AppendLine();
        }

        userSb.AppendLine();
        userSb.AppendLine("Respond strictly in valid JSON matching this schema:");
        userSb.AppendLine(@"{
  ""detectedDocumentType"": ""string (name the primary document type you identify from headings/content)"",
  ""documentTypeConfidence"": 0.95,
  ""observations"": [
    {
      ""requirementId"": ""string (must match requirement ID exactly)"",
      ""observationState"": ""present"" | ""missing"" | ""ambiguous"",
      ""evidence"": ""string (exact quote or visual description)"",
      ""confidence"": 0.95
    }
  ]
}");

        return (systemSb.ToString(), userSb.ToString());
    }
}

