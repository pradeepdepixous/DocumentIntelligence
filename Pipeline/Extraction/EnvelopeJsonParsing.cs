using System.Text.Json;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Shared JSON parsing for the markdown-first extractor response: per-page transcripts, batch
/// summary, and keywords. Used by both the Gemini and OpenAI batch extractors so the response
/// shape is handled identically regardless of which model produced it.
/// </summary>
internal static class EnvelopeJsonParsing
{
    /// <summary>
    /// Parses the "pages" array containing per-page verbatim Markdown transcripts and table indicators.
    /// </summary>
    public static List<PageMarkdownContent> ParseMarkdownPages(JsonElement root, BatchDescriptor batch)
    {
        var pages = new List<PageMarkdownContent>();

        if (root.TryGetProperty("pages", out var pagesElem) && pagesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var pElem in pagesElem.EnumerateArray())
            {
                int pageNum = batch.StartPage;
                if (pElem.TryGetProperty("page_number", out var pn) &&
                    pn.ValueKind == JsonValueKind.Number &&
                    pn.TryGetInt32(out int p))
                {
                    pageNum = Math.Clamp(p, batch.StartPage, batch.EndPage);
                }

                string md = string.Empty;
                if (pElem.TryGetProperty("markdown", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    md = m.GetString() ?? string.Empty;
                }

                bool hasTables = false;
                if (pElem.TryGetProperty("has_tables", out var ht) &&
                    (ht.ValueKind == JsonValueKind.True || ht.ValueKind == JsonValueKind.False))
                {
                    hasTables = ht.GetBoolean();
                }
                else if (md.Contains("| ---") || md.Contains("|:---"))
                {
                    hasTables = true;
                }

                if (!string.IsNullOrWhiteSpace(md))
                {
                    pages.Add(new PageMarkdownContent
                    {
                        PageNumber = pageNum,
                        Markdown = md.Trim(),
                        HasTables = hasTables
                    });
                }
            }
        }

        return pages;
    }

    /// <summary>
    /// Parses high-level batch summary string.
    /// </summary>
    public static string ParseBatchSummary(JsonElement root)
    {
        if (root.TryGetProperty("batch_summary", out var s) && s.ValueKind == JsonValueKind.String)
        {
            return s.GetString() ?? string.Empty;
        }
        if (root.TryGetProperty("summary", out var s2) && s2.ValueKind == JsonValueKind.String)
        {
            return s2.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// Parses high-level batch keywords.
    /// </summary>
    public static List<string> ParseBatchKeywords(JsonElement root)
    {
        var keywords = new List<string>();
        if (root.TryGetProperty("batch_keywords", out var kwElem) && kwElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in kwElem.EnumerateArray())
            {
                string? str = k.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    keywords.Add(str.Trim());
                }
            }
        }
        else if (root.TryGetProperty("keywords", out var kwElem2) && kwElem2.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in kwElem2.EnumerateArray())
            {
                string? str = k.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    keywords.Add(str.Trim());
                }
            }
        }
        return keywords;
    }
}

