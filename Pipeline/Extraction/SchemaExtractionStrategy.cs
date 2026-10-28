using System.Text;
using System.Text.Json;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Schema-guided, evidence-first extraction strategy.
/// Extracts structured fields, source quotes, page numbers, and tables adhering to the ExtractionPlan.
/// </summary>
public class SchemaExtractionStrategy : IExtractionStrategy
{
    public string ModeName => "schema";

    public (string SystemInstruction, string UserPrompt) BuildPrompts(BatchDescriptor batch, ExtractionPlan plan)
    {
        var fieldsSpec = new StringBuilder();
        fieldsSpec.AppendLine($"Target Document Type: {plan.TargetDocumentType}");
        fieldsSpec.AppendLine("Planned Fields to Extract:");

        foreach (var field in plan.DesiredFields)
        {
            fieldsSpec.Append($"- {field.FieldKey} ({field.FieldLabel}) [{field.ValueType}]: {field.Description}");
            if (field.Aliases.Count > 0)
            {
                fieldsSpec.Append($" (Aliases: {string.Join(", ", field.Aliases)})");
            }
            if (field.IsMandatory)
            {
                fieldsSpec.Append(" [REQUIRED]");
            }
            fieldsSpec.AppendLine();
        }

        if (plan.PreserveTables)
        {
            fieldsSpec.AppendLine("- Preserve all critical tables with their row and column structure in 'tables'.");
        }

        string tablesSection = plan.PreserveTables ? ",\n  \"tables\": []" : "";

        string systemInstruction =
            "You are an expert Document Intelligence and Structured Data Extraction Engine.\n" +
            $"You are analyzing Batch {batch.BatchId} covering pages {batch.StartPage} through {batch.EndPage}.\n\n" +
            "CORE EXTRACTION RULES:\n" +
            "1. Discovered Values: Values may appear anywhere on these pages. Match them against the planned fields based on semantic meaning.\n" +
            "2. Grounded Evidence: Every extracted value MUST be directly supported by verbatim text on these pages.\n" +
            "3. Source Attribution: For EVERY extracted item, provide the exact 'page_number' and verbatim 'source_text' (the supporting quote).\n" +
            "4. Missing Values: If a planned field is not present or supported by the text in this batch, do NOT include it in 'items' or set it to null. NEVER guess or hallucinate.\n" +
            "5. Confidence: Score each item's extraction confidence from 0.00 to 1.00 based on clarity and evidence.\n\n" +
            "TARGET FIELDS SCHEMA:\n" +
            fieldsSpec.ToString() + "\n\n" +
            "STRICT JSON OUTPUT FORMAT:\n" +
            "{\n" +
            "  \"items\": [\n" +
            "    {\n" +
            "      \"field_key\": \"exact_field_key_from_schema\",\n" +
            "      \"field_label\": \"Human Readable Title\",\n" +
            "      \"value_type\": \"string|number|date|currency|array\",\n" +
            "      \"value\": \"Extracted Value\",\n" +
            "      \"confidence\": 0.95,\n" +
            "      \"sources\": [\n" +
            "        {\n" +
            $"          \"page_number\": {batch.StartPage},\n" +
            "          \"source_text\": \"Exact verbatim sentence or phrase from the document supporting this value.\"\n" +
            "        }\n" +
            "      ]\n" +
            "    }\n" +
            $"  ]{tablesSection}\n" +
            "}";

        string userPrompt =
            $"Process pages {batch.StartPage} through {batch.EndPage}. " +
            "Extract all schema fields present in these pages with exact verbatim supporting evidence quotes and page numbers. " +
            "Output strictly valid JSON.";

        return (systemInstruction, userPrompt);
    }

    public DocumentEnvelope ParseEnvelope(string rawJson, BatchDescriptor batch, ExtractionPlan plan)
    {
        var envelope = new DocumentEnvelope
        {
            DocumentId = batch.DocumentId,
            JobId = batch.JobId,
            BatchId = batch.BatchId,
            SchemaVersion = plan.SchemaVersion > 0 ? plan.SchemaVersion : 2,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // 1. Parse structured items with source citations and confidence
            var items = EnvelopeJsonParsing.ParseItems(root, batch);
            envelope.Items.AddRange(items);

            // 2. Parse structured tables
            var tables = EnvelopeJsonParsing.ParseTables(root, batch);
            envelope.Tables.AddRange(tables);

            // 3. Parse optional markdown transcripts (if returned by model)
            var pages = EnvelopeJsonParsing.ParseMarkdownPages(root, batch);
            envelope.Pages.AddRange(pages);

            // 4. Parse batch summary and keywords
            envelope.DocumentSummary = EnvelopeJsonParsing.ParseBatchSummary(root);
            envelope.DocumentKeywords.AddRange(EnvelopeJsonParsing.ParseBatchKeywords(root));
        }
        catch (Exception ex)
        {
            envelope.Items.Add(new ExtractedItem
            {
                ItemType = "error",
                FieldKey = "json_parse_error",
                FieldLabel = "JSON Parse Error",
                Value = $"Failed to parse model JSON: {ex.Message}",
                ValidationStatus = "error"
            });
        }

        return envelope;
    }
}
