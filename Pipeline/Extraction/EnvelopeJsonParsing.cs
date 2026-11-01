using System.Linq;
using System.Text.Json;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Shared JSON parsing for batch extractor responses: structured fields, source evidence citations,
/// tables, per-page transcripts, batch summary, and keywords. Used by Gemini and OpenAI batch extractors.
/// </summary>
internal static class EnvelopeJsonParsing
{
    /// <summary>
    /// Parses an item's evidence into one SourceEvidence per contributing page. Prefers the
    /// "sources" array (one entry per page that backs the value); falls back to a legacy
    /// single "page_number"/"source_text" pair if the model did not emit "sources".
    /// </summary>
    public static List<SourceEvidence> ParseSources(JsonElement itemElem, BatchDescriptor batch)
    {
        var sources = new List<SourceEvidence>();

        if (itemElem.TryGetProperty("sources", out var sourcesElem) && sourcesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var srcElem in sourcesElem.EnumerateArray())
            {
                int pageNum = batch.StartPage;
                if (srcElem.TryGetProperty("page_number", out var pn) &&
                    pn.ValueKind == JsonValueKind.Number &&
                    pn.TryGetInt32(out int p))
                {
                    pageNum = Math.Clamp(p, batch.StartPage, batch.EndPage);
                }

                string sourceText = string.Empty;
                if (srcElem.TryGetProperty("source_text", out var st) && st.ValueKind == JsonValueKind.String)
                {
                    sourceText = st.GetString() ?? string.Empty;
                }
                else if (srcElem.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.String)
                {
                    sourceText = ev.GetString() ?? string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(sourceText))
                {
                    sources.Add(new SourceEvidence { PageNumber = pageNum, SourceText = sourceText.Trim() });
                }
            }
        }

        if (sources.Count == 0)
        {
            // Fallback: model emitted a single page_number/source_text or page/evidence pair at the item level.
            int pageNum = batch.StartPage;
            if (itemElem.TryGetProperty("page_number", out var pn) &&
                pn.ValueKind == JsonValueKind.Number &&
                pn.TryGetInt32(out int p))
            {
                pageNum = Math.Clamp(p, batch.StartPage, batch.EndPage);
            }
            else if (itemElem.TryGetProperty("page", out var pProp) &&
                     pProp.ValueKind == JsonValueKind.Number &&
                     pProp.TryGetInt32(out int pVal))
            {
                pageNum = Math.Clamp(pVal, batch.StartPage, batch.EndPage);
            }

            string sourceText = string.Empty;
            if (itemElem.TryGetProperty("source_text", out var st) && st.ValueKind == JsonValueKind.String)
            {
                sourceText = st.GetString() ?? string.Empty;
            }
            else if (itemElem.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.String)
            {
                sourceText = ev.GetString() ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                sources.Add(new SourceEvidence { PageNumber = pageNum, SourceText = sourceText.Trim() });
            }
        }

        return sources;
    }

    /// <summary>
    /// Parses the top-level "items" array into ExtractedItem entries with types, confidence, and source citations.
    /// </summary>
    public static List<ExtractedItem> ParseItems(JsonElement root, BatchDescriptor batch)
    {
        var items = new List<ExtractedItem>();

        if (!root.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            // Also check alternate property "candidates"
            if (!root.TryGetProperty("candidates", out itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return items;
            }
        }

        foreach (var itemElem in itemsElement.EnumerateArray())
        {
            string fieldKey = string.Empty;
            if (itemElem.TryGetProperty("field_key", out var fk))
                fieldKey = fk.GetString() ?? string.Empty;
            else if (itemElem.TryGetProperty("field", out var f))
                fieldKey = f.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fieldKey)) continue;

            string fieldLabel = itemElem.TryGetProperty("field_label", out var fl) ? fl.GetString() ?? fieldKey : fieldKey;
            string valueType = itemElem.TryGetProperty("value_type", out var vt) ? vt.GetString() ?? "string" : "string";

            object? val = null;
            if (itemElem.TryGetProperty("value", out var vElem))
            {
                if (vElem.ValueKind == JsonValueKind.Null || vElem.ValueKind == JsonValueKind.Undefined)
                {
                    continue; // Skip null/undefined values
                }
                val = vElem.Clone();
            }
            else
            {
                continue;
            }

            var sources = ParseSources(itemElem, batch);

            double confidence = 0.90;
            if (itemElem.TryGetProperty("confidence", out var conf) &&
                conf.ValueKind == JsonValueKind.Number &&
                conf.TryGetDouble(out double c))
            {
                confidence = Math.Clamp(c, 0.0, 1.0);
            }

            items.Add(new ExtractedItem
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

        return items;
    }

    /// <summary>
    /// Parses the top-level "tables" array into ExtractedTableItem entries, preserving row/column
    /// structure and per-cell evidence instead of discarding it.
    /// </summary>
    public static List<ExtractedTableItem> ParseTables(JsonElement root, BatchDescriptor batch)
    {
        var tables = new List<ExtractedTableItem>();

        if (!root.TryGetProperty("tables", out var tablesElem) || tablesElem.ValueKind != JsonValueKind.Array)
        {
            return tables;
        }

        foreach (var tableElem in tablesElem.EnumerateArray())
        {
            var table = new ExtractedTableItem
            {
                TableKey = tableElem.TryGetProperty("table_key", out var tk) ? tk.GetString() ?? string.Empty : string.Empty,
                TableTitle = tableElem.TryGetProperty("table_title", out var tt) && tt.ValueKind == JsonValueKind.String ? tt.GetString() : null,
                PageNumber = batch.StartPage
            };

            if (tableElem.TryGetProperty("page_number", out var pn) &&
                pn.ValueKind == JsonValueKind.Number &&
                pn.TryGetInt32(out int p))
            {
                table.PageNumber = Math.Clamp(p, batch.StartPage, batch.EndPage);
            }

            if (tableElem.TryGetProperty("headers", out var headersElem) && headersElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in headersElem.EnumerateArray())
                {
                    table.Headers.Add(h.GetString() ?? string.Empty);
                }
            }

            if (tableElem.TryGetProperty("rows", out var rowsElem) && rowsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var rowElem in rowsElem.EnumerateArray())
                {
                    var row = new Dictionary<string, ExtractedCellItem>();
                    if (rowElem.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var cellProp in rowElem.EnumerateObject())
                        {
                            row[cellProp.Name] = ParseCell(cellProp.Value);
                        }
                    }
                    table.Rows.Add(row);
                }
            }

            if (!string.IsNullOrWhiteSpace(table.TableKey) || table.Headers.Count > 0 || table.Rows.Count > 0)
            {
                tables.Add(table);
            }
        }

        return tables;
    }

    private static ExtractedCellItem ParseCell(JsonElement cellElem)
    {
        if (cellElem.ValueKind == JsonValueKind.Object)
        {
            return new ExtractedCellItem
            {
                Value = cellElem.TryGetProperty("value", out var v) ? v.Clone() : null,
                Confidence = cellElem.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out double cd)
                    ? Math.Clamp(cd, 0.0, 1.0)
                    : 0.90,
                SourceText = cellElem.TryGetProperty("source_text", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null
            };
        }

        // Model emitted a bare scalar for the cell instead of the {value, confidence, source_text} shape.
        return new ExtractedCellItem { Value = cellElem.Clone(), Confidence = 0.90 };
    }

    /// <summary>
    /// Parses the "pages" array containing per-page verbatim Markdown transcripts and table indicators.
    /// </summary>
    public static List<PageMarkdownContent> ParseMarkdownPages(JsonElement root, BatchDescriptor batch)
    {
        var pagesByNumber = new Dictionary<int, PageMarkdownContent>();

        if (root.TryGetProperty("pages", out var pagesElem) && pagesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var pElem in pagesElem.EnumerateArray())
            {
                if (!pElem.TryGetProperty("page_number", out var pn) ||
                    pn.ValueKind != JsonValueKind.Number ||
                    !pn.TryGetInt32(out int pageNum))
                {
                    // No usable page number — there's nothing safe to attribute this content to.
                    continue;
                }

                if (pageNum < batch.StartPage || pageNum > batch.EndPage)
                {
                    // The model reported a page number outside this batch's actual range.
                    // Clamping it to the nearest boundary page would silently attribute the
                    // wrong content to a real page (and could win over correct content during
                    // stitching, which keeps whichever transcript is longer). Drop it instead —
                    // the true page then shows up as missing and gets retried/flagged like any
                    // other incomplete page.
                    continue;
                }

                string md = string.Empty;
                if (pElem.TryGetProperty("markdown", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    md = m.GetString() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(md))
                {
                    continue;
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

                var page = new PageMarkdownContent
                {
                    PageNumber = pageNum,
                    Markdown = md.Trim(),
                    HasTables = hasTables
                };

                // The model can emit the same page number twice in one response; keep whichever
                // transcript is more complete instead of silently taking the last one.
                if (!pagesByNumber.TryGetValue(pageNum, out var existing) || page.Markdown.Length > existing.Markdown.Length)
                {
                    pagesByNumber[pageNum] = page;
                }
            }
        }

        return pagesByNumber.Values.OrderBy(p => p.PageNumber).ToList();
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
