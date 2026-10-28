using System.Text.Json;
using Microsoft.Extensions.Logging;
using PixousDocIntelligence.DocumentGeneration.Models;
using PixousDocIntelligence.DocumentGeneration.Validation;
using PixousDocIntelligence.Services.LLM;

namespace PixousDocIntelligence.DocumentGeneration.Services;

public class ContentGenerationException : Exception
{
    public int StatusCode { get; }
    public ContentGenerationException(string message, int statusCode = 500) : base(message)
    {
        StatusCode = statusCode;
    }
}

public interface IAiContentGenerator
{
    Task<StructuredDocumentContent> GenerateContentAsync(
        DocumentTemplateDefinition template, 
        string instructions, 
        ILLMClient llmClient, 
        CancellationToken cancellationToken = default);
}

public class AiContentGenerator : IAiContentGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IContentValidator _contentValidator;
    private readonly ILogger<AiContentGenerator> _logger;

    public AiContentGenerator(IContentValidator contentValidator, ILogger<AiContentGenerator> logger)
    {
        _contentValidator = contentValidator;
        _logger = logger;
    }

    public async Task<StructuredDocumentContent> GenerateContentAsync(
        DocumentTemplateDefinition template, 
        string instructions, 
        ILLMClient llmClient, 
        CancellationToken cancellationToken = default)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new ContentGenerationException("Generation instructions cannot be empty.", 400);
        }

        string systemInstruction = BuildSystemInstruction();
        string userPrompt = BuildUserPrompt(template, instructions);

        int maxAttempts = 2;
        StructuredDocumentContent? structuredContent = null;
        string? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation("Generating structured content for template '{TemplateId}', attempt {Attempt}...", template.TemplateId, attempt);
                
                string promptToSend = attempt == 1 
                    ? userPrompt 
                    : $"{userPrompt}\n\nIMPORTANT: The previous output failed with error: {lastError}. You must output strictly valid JSON conforming exactly to the template schema.";

                string rawLlmResponse = await llmClient.GenerateContentAsync(systemInstruction, promptToSend, requireJson: true, cancellationToken);
                string cleanedJson = CleanJsonResponse(rawLlmResponse);

                structuredContent = ParseLlmResponse(cleanedJson, template);
                if (structuredContent != null)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex, "Attempt {Attempt} to generate content failed: {Message}", attempt, ex.Message);
            }
        }

        if (structuredContent == null)
        {
            throw new ContentGenerationException($"Failed to generate valid structured content after {maxAttempts} attempts: {lastError}", 502);
        }

        return structuredContent;
    }

    private static string BuildSystemInstruction()
    {
        return @"You are an AI document content generator.

Your task is to generate structured document content based on:
1. A document template definition.
2. The user's plain-language instructions.

Rules:
- Follow the supplied template structure.
- Include every required section.
- Use the exact section identifiers and column field names from the template.
- Do not add unsupported fields.
- For factual or signatory details (such as real external names, official office addresses, reference IDs, or explicit signatory approvals): do not invent them if omitted; set them to null and list in 'missing_fields'.
- For content fields, tables, lists, and narrative descriptions: proactively synthesize and draft thorough, professional content that fulfills the user's instructions and matches the section definitions.
- Identify genuinely missing information required to complete the document and add each field path to 'missing_fields' (e.g. 'recipient.address', 'signature.name').
- Return only valid JSON matching the specified output schema.
- Do not generate markdown fences or formatting code in the JSON response.

Output JSON format:
{
  ""template_id"": ""<template_id>"",
  ""template_version"": <version>,
  ""content"": {
    ""<section_id>"": ...
  },
  ""missing_fields"": [
    ""<missing_field_path>""
  ]
}";
    }

    private static string BuildUserPrompt(DocumentTemplateDefinition template, string instructions)
    {
        string templateJson = JsonSerializer.Serialize(new
        {
            template_id = template.TemplateId,
            version = template.Version,
            document_type = template.DocumentType,
            content_schema = template.ContentSchema
        }, JsonOptions);

        return $@"Template Definition:
{templateJson}

User Instructions:
{instructions}

Instructions:
Generate the structured content JSON conforming strictly to the template above. Draft professional, thorough content matching the section types (paragraphs, bullet lists, tables, objects) based on the user's instructions. For any mandatory factual fields not supplied or inferable, list them in 'missing_fields'. Output strictly valid JSON only.";
    }

    private static StructuredDocumentContent ParseLlmResponse(string rawJson, DocumentTemplateDefinition template)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var structured = new StructuredDocumentContent
        {
            TemplateId = template.TemplateId,
            TemplateVersion = template.Version,
            Content = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            MissingFields = new List<string>()
        };

        if (root.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in contentElement.EnumerateObject())
            {
                structured.Content[prop.Name] = prop.Value.Clone();
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // If the LLM returned the sections directly at the root
            foreach (var prop in root.EnumerateObject())
            {
                if (!prop.Name.Equals("template_id", StringComparison.OrdinalIgnoreCase) &&
                    !prop.Name.Equals("template_version", StringComparison.OrdinalIgnoreCase) &&
                    !prop.Name.Equals("missing_fields", StringComparison.OrdinalIgnoreCase))
                {
                    structured.Content[prop.Name] = prop.Value.Clone();
                }
            }
        }

        if (root.TryGetProperty("missing_fields", out var missingElement) && missingElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in missingElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? str = item.GetString();
                    if (!string.IsNullOrWhiteSpace(str)) structured.MissingFields.Add(str);
                }
            }
        }

        return structured;
    }

    private static string CleanJsonResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        string trimmed = raw.Trim();

        // Strip markdown ```json code fences if present
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(3);
        }

        if (trimmed.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }

        return trimmed.Trim();
    }
}
