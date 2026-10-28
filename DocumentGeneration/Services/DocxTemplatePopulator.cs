using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Services;

public interface IDocxTemplatePopulator
{
    byte[] PopulateDocx(byte[] templateBytes, DocumentTemplateDefinition template, Dictionary<string, JsonElement> content);
}

public class DocxTemplatePopulator : IDocxTemplatePopulator
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{([a-zA-Z0-9_\.\:\#\/]+)\}\}", RegexOptions.Compiled);

    public byte[] PopulateDocx(byte[] templateBytes, DocumentTemplateDefinition template, Dictionary<string, JsonElement> content)
    {
        if (templateBytes == null || templateBytes.Length == 0)
        {
            throw new ArgumentException("Template bytes cannot be null or empty.", nameof(templateBytes));
        }

        using var stream = new MemoryStream();
        stream.Write(templateBytes, 0, templateBytes.Length);

        using (var wordDoc = WordprocessingDocument.Open(stream, true))
        {
            var mainPart = wordDoc.MainDocumentPart;
            if (mainPart?.Document?.Body != null)
            {
                var body = mainPart.Document.Body;

                // 1. Populate Word Content Controls (SDT)
                PopulateContentControls(body, content);

                // 2. Populate Header and Footer Content Controls
                foreach (var hp in mainPart.HeaderParts)
                {
                    if (hp.Header != null) PopulateContentControls(hp.Header, content);
                }
                foreach (var fp in mainPart.FooterParts)
                {
                    if (fp.Footer != null) PopulateContentControls(fp.Footer, content);
                }

                // 3. Populate Dynamic Tables (both SDT and Text Placeholder tables)
                PopulateTables(body, content);

                // 4. Populate Fallback Text Placeholders in Paragraphs
                PopulateTextPlaceholders(body, content);
                foreach (var hp in mainPart.HeaderParts)
                {
                    if (hp.Header != null) PopulateTextPlaceholders(hp.Header, content);
                }
                foreach (var fp in mainPart.FooterParts)
                {
                    if (fp.Footer != null) PopulateTextPlaceholders(fp.Footer, content);
                }

                mainPart.Document.Save();
            }
        }

        return stream.ToArray();
    }

    private static void PopulateContentControls(OpenXmlElement container, Dictionary<string, JsonElement> content)
    {
        var sdtList = container.Descendants<SdtElement>().ToList();
        foreach (var sdt in sdtList)
        {
            string? tag = sdt.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value;
            string? alias = sdt.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value;
            string key = (tag ?? alias)?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(key)) continue;

            if (content.TryGetValue(key, out var jsonVal))
            {
                if (jsonVal.ValueKind == JsonValueKind.Array)
                {
                    var items = jsonVal.EnumerateArray().ToList();
                    // If items are strings -> Multi-paragraph or bullet list
                    if (items.Count > 0 && items[0].ValueKind == JsonValueKind.String)
                    {
                        var parentP = sdt.Ancestors<Paragraph>().FirstOrDefault() ?? sdt.Descendants<Paragraph>().FirstOrDefault();
                        if (parentP != null && parentP.Parent != null)
                        {
                            var parentContainer = parentP.Parent;
                            var refNode = parentP;
                            foreach (var item in items)
                            {
                                var newP = (Paragraph)parentP.CloneNode(true);
                                SetParagraphPlainText(newP, item.GetString() ?? "");
                                parentContainer.InsertAfter(newP, refNode);
                                refNode = newP;
                            }
                            parentP.Remove();
                        }
                    }
                }
                else if (jsonVal.ValueKind == JsonValueKind.Object)
                {
                    // Render key-value summary
                    string text = string.Join("\n", jsonVal.EnumerateObject().Select(p => $"{p.Name}: {p.Value}"));
                    SetSdtPlainText(sdt, text);
                }
                else
                {
                    // Single string / number / date
                    string text = jsonVal.ValueKind == JsonValueKind.String ? jsonVal.GetString() ?? "" : jsonVal.ToString();
                    SetSdtPlainText(sdt, text);
                }
            }
        }
    }

    private static void PopulateTables(OpenXmlElement container, Dictionary<string, JsonElement> content)
    {
        var tables = container.Descendants<Table>().ToList();
        foreach (var tbl in tables)
        {
            var rows = tbl.Descendants<TableRow>().ToList();
            if (rows.Count < 2) continue;

            // Search for rows that contain table placeholders
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                var cells = row.Descendants<TableCell>().ToList();

                // Check placeholders in this row
                string rowText = row.InnerText;
                var matches = PlaceholderRegex.Matches(rowText);

                string? targetArrayKey = null;
                var colMappings = new Dictionary<string, string>(); // placeholder -> property_name

                foreach (Match m in matches)
                {
                    string rawTag = m.Groups[1].Value.Trim().TrimStart('#', '/');
                    if (rawTag.StartsWith("table:", StringComparison.OrdinalIgnoreCase))
                    {
                        rawTag = rawTag.Substring(6);
                    }

                    if (rawTag.Contains('.'))
                    {
                        var parts = rawTag.Split('.');
                        targetArrayKey = parts[0];
                        colMappings[m.Value] = parts[1];
                    }
                    else
                    {
                        colMappings[m.Value] = rawTag;
                    }
                }

                // If not prefixed with dot, check if any array in content has matching properties
                if (targetArrayKey == null && colMappings.Count > 0)
                {
                    foreach (var kvp in content)
                    {
                        if (kvp.Value.ValueKind == JsonValueKind.Array)
                        {
                            var firstObj = kvp.Value.EnumerateArray().FirstOrDefault();
                            if (firstObj.ValueKind == JsonValueKind.Object)
                            {
                                var propNames = firstObj.EnumerateObject().Select(p => p.Name).ToList();
                                if (colMappings.Values.Any(col => propNames.Contains(col, StringComparer.OrdinalIgnoreCase)))
                                {
                                    targetArrayKey = kvp.Key;
                                    break;
                                }
                            }
                        }
                    }
                }

                // If target array found in content, duplicate row for each array item!
                if (targetArrayKey != null && content.TryGetValue(targetArrayKey, out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
                {
                    var items = arrayElement.EnumerateArray().ToList();
                    var refRow = row;

                    foreach (var item in items)
                    {
                        var newRow = (TableRow)row.CloneNode(true);
                        foreach (var cell in newRow.Descendants<TableCell>())
                        {
                            foreach (var p in cell.Descendants<Paragraph>())
                            {
                                NormalizeParagraphRuns(p);
                                string pText = p.InnerText;
                                foreach (var map in colMappings)
                                {
                                    if (pText.Contains(map.Key))
                                    {
                                        string cellVal = "";
                                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(map.Value, out var val))
                                        {
                                            cellVal = val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
                                        }
                                        pText = pText.Replace(map.Key, cellVal);
                                    }
                                }
                                SetParagraphPlainText(p, pText);
                            }
                        }
                        tbl.InsertAfter(newRow, refRow);
                        refRow = newRow;
                    }

                    // Remove the placeholder template row
                    row.Remove();
                    break;
                }
            }
        }
    }

    private static void PopulateTextPlaceholders(OpenXmlElement container, Dictionary<string, JsonElement> content)
    {
        var paragraphs = container.Descendants<Paragraph>().ToList();
        foreach (var p in paragraphs)
        {
            if (p.Parent == null) continue;

            NormalizeParagraphRuns(p);
            string fullText = p.InnerText;
            if (string.IsNullOrEmpty(fullText) || !fullText.Contains("{{")) continue;

            var matches = PlaceholderRegex.Matches(fullText);
            if (matches.Count == 0) continue;

            bool handledArray = false;

            foreach (Match m in matches)
            {
                string tag = m.Groups[1].Value.Trim().TrimStart('#', '/');
                if (tag.StartsWith("table:", StringComparison.OrdinalIgnoreCase)) continue;

                if (content.TryGetValue(tag, out var val))
                {
                    if (val.ValueKind == JsonValueKind.Array)
                    {
                        var items = val.EnumerateArray().ToList();
                        if (items.Count > 0 && items[0].ValueKind == JsonValueKind.String)
                        {
                            var parent = p.Parent;
                            var refNode = p;
                            foreach (var item in items)
                            {
                                var newP = (Paragraph)p.CloneNode(true);
                                SetParagraphPlainText(newP, item.GetString() ?? "");
                                parent.InsertAfter(newP, refNode);
                                refNode = newP;
                            }
                            p.Remove();
                            handledArray = true;
                            break;
                        }
                    }
                    else
                    {
                        string replacement = val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
                        fullText = fullText.Replace(m.Value, replacement);
                    }
                }
            }

            if (!handledArray && p.Parent != null)
            {
                SetParagraphPlainText(p, fullText);
            }
        }
    }

    private static void SetSdtPlainText(SdtElement sdt, string text)
    {
        var runs = sdt.Descendants<Run>().ToList();
        if (runs.Count > 0)
        {
            var firstRun = runs[0];
            var rPr = firstRun.RunProperties != null ? (RunProperties)firstRun.RunProperties.CloneNode(true) : null;

            var contentBlock = sdt.GetFirstChild<SdtContentBlock>() ?? (OpenXmlElement?)sdt.GetFirstChild<SdtContentRun>() ?? sdt;
            contentBlock.RemoveAllChildren();

            var newRun = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            if (rPr != null) newRun.RunProperties = rPr;

            var p = new Paragraph(newRun);
            contentBlock.AppendChild(p);
        }
        else
        {
            var p = new Paragraph(new Run(new Text(text)));
            sdt.AppendChild(p);
        }
    }

    private static void SetParagraphPlainText(Paragraph p, string text)
    {
        var runs = p.Descendants<Run>().ToList();
        RunProperties? existingRPr = null;
        if (runs.Count > 0 && runs[0].RunProperties != null)
        {
            existingRPr = (RunProperties)runs[0].RunProperties!.CloneNode(true);
        }

        p.RemoveAllChildren<Run>();

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var run = new Run(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            if (existingRPr != null) run.RunProperties = (RunProperties)existingRPr.CloneNode(true);
            p.AppendChild(run);

            if (i < lines.Length - 1)
            {
                p.AppendChild(new Break());
            }
        }
    }

    private static void NormalizeParagraphRuns(Paragraph p)
    {
        var runs = p.Elements<Run>().ToList();
        if (runs.Count <= 1) return;

        // Group runs that share the same run properties and merge text
        var combinedText = string.Concat(runs.Select(r => r.InnerText));
        if (combinedText.Contains("{{"))
        {
            var firstRun = runs[0];
            var rPr = firstRun.RunProperties != null ? (RunProperties)firstRun.RunProperties.CloneNode(true) : null;

            p.RemoveAllChildren<Run>();

            var newRun = new Run(new Text(combinedText) { Space = SpaceProcessingModeValues.Preserve });
            if (rPr != null) newRun.RunProperties = rPr;

            p.AppendChild(newRun);
        }
    }
}
