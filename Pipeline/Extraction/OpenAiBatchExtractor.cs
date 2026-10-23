using System.Text;
using System.Text.Json;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Services.LLM;
using UglyToad.PdfPig;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Phase 4 OpenAI Batch Extractor.
/// Extracts text from batch PDF bytes using PdfPig, and sends structured prompts to OpenAI
/// enforcing JSON Schema / json_object output format.
/// </summary>
public class OpenAiBatchExtractor : IBatchExtractor
{
    private readonly ILLMClient _llmClient;

    public OpenAiBatchExtractor(ILLMClient llmClient)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
    }

    public async Task<DocumentEnvelope> ExtractBatchAsync(
        byte[] batchPdfBytes, 
        BatchDescriptor batchDescriptor, 
        ExtractionPlan plan, 
        CancellationToken cancellationToken = default)
    {
        // 1. Extract text from the batch slice using PdfPig
        var sb = new StringBuilder();
        try
        {
            using var pdfDoc = PdfDocument.Open(batchPdfBytes);
            int pageIndex = batchDescriptor.StartPage;
            foreach (var page in pdfDoc.GetPages())
            {
                sb.AppendLine($"=== PAGE {pageIndex} ===");
                sb.AppendLine(page.Text);
                pageIndex++;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to extract text from batch slice {batchDescriptor.BatchId}: {ex.Message}", ex);
        }

        string batchText = sb.ToString();

        // 2. Prepare structured system instruction and user prompt
        string systemInstruction = 
            "You are an expert Document Intelligence and Markdown Reconstruction Engine.\n" +
            "Your task is to transcribe each page of the provided document text into clean, structured GitHub Flavored Markdown (preserving headings #, ##, bold text, bullet points, and tables).\n\n" +
            "RULES:\n" +
            "1. Transcribe the text for each page into clean Markdown format.\n" +
            "2. Preserve all tables using Markdown table syntax (| Header 1 | Header 2 |).\n" +
            "3. Extract high-level keywords summarizing the main topics of this batch.\n" +
            "4. Provide a concise executive summary of this batch.\n" +
            "5. Output strictly a JSON object matching the required schema:\n" +
            "{\n" +
            "  \"batch_summary\": \"Concise summary of pages in this batch...\",\n" +
            "  \"batch_keywords\": [\"keyword1\", \"keyword2\", \"keyword3\"],\n" +
            "  \"pages\": [\n" +
            "    {\n" +
            $"      \"page_number\": {batchDescriptor.StartPage},\n" +
            "      \"has_tables\": false,\n" +
            "      \"markdown\": \"# Page Heading\\n\\nVerbatim page text in markdown format...\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"items\": [],\n" +
            "  \"tables\": []\n" +
            "}";

        string userPrompt = 
            $"DOCUMENT BATCH TEXT ({batchDescriptor.PageRangeLabel}):\n{batchText}\n\n" +
            $"Transcribe each page from {batchDescriptor.StartPage} to {batchDescriptor.EndPage} into structured Markdown and return strictly conforming JSON.";

        // 3. Call OpenAI with JSON mode
        string responseJson = await _llmClient.GenerateContentAsync(systemInstruction, userPrompt, requireJson: true, cancellationToken);

        // 4. Parse into verified DocumentEnvelope
        return ParseEnvelope(responseJson, batchDescriptor, plan);
    }

    private static string FormatPlanForPrompt(ExtractionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Target Document Type: {plan.TargetDocumentType}");
        sb.AppendLine($"Extraction Mode: {plan.ExtractionMode}");
        sb.AppendLine("Planned Fields:");

        foreach (var field in plan.DesiredFields)
        {
            sb.Append($"- {field.FieldKey} ({field.FieldLabel}) [{field.ValueType}]: {field.Description}");
            if (field.Aliases.Count > 0)
            {
                sb.Append($" (Aliases: {string.Join(", ", field.Aliases)})");
            }
            sb.AppendLine();
        }

        if (plan.PreserveTables)
        {
            sb.AppendLine("Preserve all relevant tables with their row and column structure.");
        }

        return sb.ToString();
    }

    private static DocumentEnvelope ParseEnvelope(string rawJson, BatchDescriptor batch, ExtractionPlan plan)
    {
        var envelope = new DocumentEnvelope
        {
            DocumentId = batch.DocumentId,
            JobId = batch.JobId,
            BatchId = batch.BatchId,
            SchemaVersion = 2,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // 1. Parse per-page Markdown transcripts
            var pages = EnvelopeJsonParsing.ParseMarkdownPages(root, batch);
            envelope.Pages = pages;

            // 2. Parse batch summary and keywords
            string summary = EnvelopeJsonParsing.ParseBatchSummary(root);
            var keywords = EnvelopeJsonParsing.ParseBatchKeywords(root);
            envelope.DocumentSummary = summary;
            envelope.DocumentKeywords = keywords;

            envelope.Batches.Add(new BatchMarkdownResult
            {
                BatchId = batch.BatchId,
                StartPage = batch.StartPage,
                EndPage = batch.EndPage,
                BatchSummary = summary,
                BatchKeywords = keywords,
                Pages = pages
            });

            // 3. Backward-compatible items & tables
            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var itemElem in itemsElement.EnumerateArray())
                {
                    string fieldKey = itemElem.TryGetProperty("field_key", out var fk) ? fk.GetString() ?? string.Empty : string.Empty;
                    string fieldLabel = itemElem.TryGetProperty("field_label", out var fl) ? fl.GetString() ?? fieldKey : fieldKey;
                    string valueType = itemElem.TryGetProperty("value_type", out var vt) ? vt.GetString() ?? "string" : "string";

                    object? val = null;
                    if (itemElem.TryGetProperty("value", out var vElem))
                    {
                        if (vElem.ValueKind == JsonValueKind.Null || vElem.ValueKind == JsonValueKind.Undefined)
                        {
                            continue;
                        }
                        val = vElem.Clone();
                    }
                    else
                    {
                        continue;
                    }

                    var sources = EnvelopeJsonParsing.ParseSources(itemElem, batch);

                    double confidence = 0.95;
                    if (itemElem.TryGetProperty("confidence", out var conf) && 
                        conf.ValueKind == JsonValueKind.Number && 
                        conf.TryGetDouble(out double c))
                    {
                        confidence = Math.Clamp(c, 0.0, 1.0);
                    }

                    if (!string.IsNullOrWhiteSpace(fieldKey) && val != null)
                    {
                        envelope.Items.Add(new ExtractedItem
                        {
                            ItemType = "field",
                            FieldKey = fieldKey,
                            FieldLabel = fieldLabel,
                            Value = val,
                            ValueType = valueType,
                            Sources = sources,
                            Confidence = new ConfidenceSignal
                            {
                                Value = confidence,
                                Source = "model_reported"
                            },
                            ValidationStatus = confidence >= 0.85 ? "valid" : "flagged"
                        });
                    }
                }
            }

            envelope.Tables.AddRange(EnvelopeJsonParsing.ParseTables(root, batch));
        }
        catch (JsonException) { /* Return envelope with items found */ }

        return envelope;
    }
}
