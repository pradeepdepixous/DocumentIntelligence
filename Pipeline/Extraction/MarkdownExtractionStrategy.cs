using System.Text.Json;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Full-content Markdown reconstruction extraction strategy.
/// Transcribes each page into verbatim GitHub Flavored Markdown (preserving headings, tables, formatting).
/// </summary>
public class MarkdownExtractionStrategy : IExtractionStrategy
{
    public string ModeName => "markdown";

    public (string SystemInstruction, string UserPrompt) BuildPrompts(BatchDescriptor batch, ExtractionPlan plan)
    {
        string systemInstruction =
            "You are an expert Document Intelligence and Markdown Reconstruction Engine.\n" +
            $"You are analyzing Batch {batch.BatchId} of a larger document. " +
            $"The batch covers original document pages {batch.StartPage} through {batch.EndPage}.\n\n" +
            "CORE MISSION:\n" +
            "1. Transcribe each page into structured GitHub Flavored Markdown (headings #, ##, bold text, bullet points, numbered lists).\n" +
            "2. Preserve ALL tables using standard Markdown table syntax (| Header 1 | Header 2 |) with full cell fidelity.\n" +
            "3. Extract high-level keywords/topics that summarize this batch.\n" +
            "4. Provide a concise executive summary of the content in this batch.\n\n" +
            "STRICT JSON OUTPUT FORMAT:\n" +
            "{\n" +
            "  \"batch_summary\": \"Concise summary of pages in this batch...\",\n" +
            "  \"batch_keywords\": [\"keyword1\", \"keyword2\", \"keyword3\"],\n" +
            "  \"pages\": [\n" +
            "    {\n" +
            $"      \"page_number\": {batch.StartPage},\n" +
            "      \"has_tables\": false,\n" +
            "      \"markdown\": \"# Page Heading\\n\\nVerbatim page text in markdown format...\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"items\": [],\n" +
            "  \"tables\": []\n" +
            "}\n\n" +
            $"CRITICAL REQUIREMENT: For every single page from {batch.StartPage} to {batch.EndPage}, " +
            "there MUST be a corresponding entry in the 'pages' array. Capture all text completely without skipping.";

        string userPrompt =
            $"Process pages {batch.StartPage} through {batch.EndPage}. " +
            "Transcribe each page into structured markdown, extract key tables, keywords, and provide a batch summary. " +
            "Output strictly conforming JSON.";

        return (systemInstruction, userPrompt);
    }

    public DocumentEnvelope ParseEnvelope(string rawJson, BatchDescriptor batch, ExtractionPlan plan)
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
