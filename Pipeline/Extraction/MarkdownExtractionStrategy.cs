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
            "You are a high-fidelity Document Transcription and Markdown Reconstruction Engine.\n" +
            $"You are processing Batch {batch.BatchId} from a larger document. " +
            $"The batch contains original document pages {batch.StartPage} through {batch.EndPage}.\n\n" +
            "PRIMARY OBJECTIVE:\n" +
            "Reproduce the actual content visible on every page as faithfully and completely as possible. " +
            "Your responsibility is transcription and structural preservation, NOT summarization, interpretation, or rewriting. " +
            "A downstream stage handles summarization separately — do not replace detailed source content with a summary.\n\n" +
            "1. COMPLETE PAGE TRANSCRIPTION\n" +
            "For every page, capture all visible content, including: titles, headings, paragraphs, and sentences; " +
            "small text, labels, annotations, footnotes, and fine print; headers, footers, printed page numbers, dates, " +
            "and reference numbers; bullet points, numbered lists, and form field labels/values; text inside tables, " +
            "charts, diagrams, figures, and stamps, to the extent it is legible; and repeated text appearing on other " +
            "pages — never omit it merely because it appeared earlier. Preserve the original wording, numbers, units, " +
            "dates, names, identifiers, and punctuation. Do not paraphrase, simplify, normalize, correct grammar, infer " +
            "missing words, or replace detailed content with a high-level description. Do not omit content because it " +
            "seems unimportant, repetitive, or technical.\n\n" +
            "2. PRESERVE DOCUMENT STRUCTURE\n" +
            "Reconstruct each page using GitHub Flavored Markdown: reflect the visible heading hierarchy, preserve " +
            "paragraph boundaries and original reading order, preserve bullet/numbered lists, and keep labels next to " +
            "their corresponding values. For multi-column layouts, follow logical reading order rather than mixing " +
            "unrelated text across columns. Do not invent headings or structure not supported by the page.\n\n" +
            "3. TABLE EXTRACTION\n" +
            "Reproduce every table with all legible rows, columns, headers, cell values, totals, and units, preserving " +
            "the relationship between each value and its row/column. Do not drop rows or columns that establish table " +
            "structure, merge distinct rows, or replace a detailed table with a summary. Preserve identifiers, decimal " +
            "places, signs, percentages, currency symbols, and dates exactly as displayed. If a table continues across " +
            "pages, transcribe the portion visible on each page independently. Use standard Markdown table syntax " +
            "(| Header 1 | Header 2 |) where practical; if a table cannot be represented faithfully in Markdown, " +
            "describe its layout and preserve all recoverable cell content rather than silently omitting it. Set " +
            "\"has_tables\" to true when a page contains at least one table.\n\n" +
            "4. CHARTS, DIAGRAMS, AND VISUAL ELEMENTS\n" +
            "Do not skip non-paragraph content. For charts, figures, flowcharts, ER diagrams, org charts, and other " +
            "visual structures, transcribe every legible label, legend, annotation, axis label, unit, value, and " +
            "caption, and preserve the visible relationships between labels, values, boxes/nodes, and connectors/arrows " +
            "— immediately after any surrounding caption text. Distinguish actual visible content from interpretation, " +
            "and do not invent unreadable labels or relationships that cannot be established from the image.\n\n" +
            "5. UNCERTAIN OR UNREADABLE CONTENT\n" +
            "Never guess or fabricate text. When a word, number, or section cannot be read confidently, preserve the " +
            "surrounding content and mark the uncertain portion inline as [unclear]. Do not mark an entire page as " +
            "unreadable when only a small region is unclear.\n\n" +
            "6. PAGE COMPLETENESS\n" +
            $"Create exactly one entry in the 'pages' array for every original page number from {batch.StartPage} " +
            $"through {batch.EndPage}, inclusive, using the original document page numbers. Never omit a page because " +
            "it is blank, repetitive, or contains only a table or images. For a genuinely blank page, set its markdown " +
            "to \"[Blank page]\". If part of a page is not fully recoverable, transcribe what is legible and mark the " +
            "limitation with [unclear] rather than inventing content to fill gaps.\n\n" +
            "STRICT JSON OUTPUT FORMAT (return valid JSON only — no commentary or code fences outside the object):\n" +
            "{\n" +
            "  \"pages\": [\n" +
            "    {\n" +
            $"      \"page_number\": {batch.StartPage},\n" +
            "      \"has_tables\": false,\n" +
            "      \"markdown\": \"# Page Heading\\n\\nComplete verbatim transcription of the original page in markdown...\"\n" +
            "    }\n" +
            "  ]\n" +
            "}\n\n" +
            $"CRITICAL REQUIREMENT: For every single page from {batch.StartPage} to {batch.EndPage}, there MUST be a " +
            "corresponding entry in the 'pages' array, and its markdown must be a detailed transcription, not a " +
            "summary. Final priority: source fidelity and completeness first, formatting second — never sacrifice " +
            "detailed transcription for brevity.";

        string userPrompt =
            $"Process pages {batch.StartPage} through {batch.EndPage}. " +
            "Transcribe each page verbatim into structured markdown, preserving every table, label, and visible " +
            "detail, and marking anything illegible as [unclear]. Output strictly conforming JSON with no summary.";

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
            // Deliberately left as an empty envelope (no Items) rather than recording an "error"
            // item here: ConfidenceAwareStitcher treats any non-empty Items across any batch as
            // schema mode and would merge a stray error item into the final stitched envelope,
            // which then makes the orchestrator's markdown-mode completeness check skip itself
            // for the whole document. An empty Pages list already reads as "incomplete" to the
            // orchestrator's retry/split recovery and its missing-page review check, which is
            // the correct and sufficient signal for a malformed or truncated response.
            Console.Error.WriteLine(
                $"[MarkdownExtractionStrategy] Failed to parse model JSON for {batch.BatchId} (pages {batch.StartPage}-{batch.EndPage}): {ex.Message}");
        }

        return envelope;
    }
}
