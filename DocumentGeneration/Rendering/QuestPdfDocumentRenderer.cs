using System.Text.Json;
using System.Text.RegularExpressions;
using PixousDocIntelligence.DocumentGeneration.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PixousDocIntelligence.DocumentGeneration.Rendering;

public class QuestPdfDocumentRenderer : IDocumentRenderer
{
    public string Format => "pdf";

    static QuestPdfDocumentRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(DocumentTemplateDefinition template, Dictionary<string, JsonElement> content, string markdown)
    {
        var primaryColor = ParseColorOrDefault(template.Presentation?.PrimaryColor, "#1E293B");
        var pageSize = template.Presentation?.PageSize?.Equals("Letter", StringComparison.OrdinalIgnoreCase) == true
            ? PageSizes.Letter
            : PageSizes.A4;

        float topMargin = template.Presentation?.Margins?.Top ?? 20f;
        float bottomMargin = template.Presentation?.Margins?.Bottom ?? 20f;
        float leftMargin = template.Presentation?.Margins?.Left ?? 20f;
        float rightMargin = template.Presentation?.Margins?.Right ?? 20f;

        string fontFamily = template.Presentation?.FontFamily ?? "Arial";

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.MarginTop(topMargin, Unit.Millimetre);
                page.MarginBottom(bottomMargin, Unit.Millimetre);
                page.MarginLeft(leftMargin, Unit.Millimetre);
                page.MarginRight(rightMargin, Unit.Millimetre);
                page.PageColor(Colors.White);

                page.DefaultTextStyle(x =>
                {
                    x = x.FontSize(10.5f).FontColor(Colors.Grey.Darken3);
                    return string.IsNullOrWhiteSpace(fontFamily) ? x : x.FontFamily(fontFamily);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    RenderMarkdownLines(col, markdown, primaryColor);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                    t.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium);
                    t.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void RenderMarkdownLines(ColumnDescriptor col, string markdown, string primaryColor)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            string line = lines[i].TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                col.Item().PaddingBottom(4);
                i++;
                continue;
            }

            // Check for Markdown Table
            if (line.TrimStart().StartsWith("|"))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }
                RenderTable(col, tableLines, primaryColor);
                continue;
            }

            if (line.StartsWith("# "))
            {
                string heading = line.Substring(2).Trim();
                col.Item().PaddingTop(10).PaddingBottom(4).Text(t =>
                {
                    t.Span(heading).Bold().FontSize(15).FontColor(primaryColor);
                });
            }
            else if (line.StartsWith("## "))
            {
                string heading = line.Substring(3).Trim();
                col.Item().PaddingTop(8).PaddingBottom(3).Text(t =>
                {
                    t.Span(heading).Bold().FontSize(13).FontColor(primaryColor);
                });
            }
            else if (line.StartsWith("### "))
            {
                string heading = line.Substring(4).Trim();
                col.Item().PaddingTop(6).PaddingBottom(2).Text(t =>
                {
                    t.Span(heading).Bold().FontSize(11.5f).FontColor(primaryColor);
                });
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                string itemText = line.Substring(2).Trim();
                col.Item().PaddingLeft(12).Row(row =>
                {
                    row.ConstantItem(12).Text("•").Bold().FontColor(primaryColor);
                    row.RelativeItem().Text(t => RenderInlineMarkdown(t, itemText, primaryColor));
                });
            }
            else
            {
                col.Item().Text(t => RenderInlineMarkdown(t, line, primaryColor));
            }

            i++;
        }
    }

    private static void RenderTable(ColumnDescriptor col, List<string> tableLines, string primaryColor)
    {
        var parsedRows = new List<List<string>>();
        foreach (var line in tableLines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("|") && trimmed.EndsWith("|") && trimmed.Length >= 2)
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }
            var cells = trimmed.Split('|').Select(c => c.Trim()).ToList();
            if (cells.All(c => c.All(ch => ch == '-' || ch == ':' || char.IsWhiteSpace(ch))))
            {
                continue; // skip separator
            }
            parsedRows.Add(cells);
        }

        if (parsedRows.Count == 0) return;
        int colCount = parsedRows.Max(r => r.Count);

        col.Item().PaddingTop(4).PaddingBottom(8).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (int c = 0; c < colCount; c++)
                {
                    columns.RelativeColumn();
                }
            });

            // Header row
            var headerRow = parsedRows[0];
            table.Header(header =>
            {
                for (int c = 0; c < colCount; c++)
                {
                    string hText = c < headerRow.Count ? headerRow[c] : "";
                    header.Cell().Background(Colors.Grey.Lighten4).BorderBottom(1).BorderColor(Colors.Grey.Medium).Padding(4).Text(t =>
                    {
                        t.Span(hText).Bold().FontSize(9f).FontColor(primaryColor);
                    });
                }
            });

            // Data rows
            for (int r = 1; r < parsedRows.Count; r++)
            {
                var rowData = parsedRows[r];
                for (int c = 0; c < colCount; c++)
                {
                    string cText = c < rowData.Count ? rowData[c] : "";
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8.5f));
                        RenderInlineMarkdown(t, cText, primaryColor);
                    });
                }
            }
        });
    }

    private static void RenderInlineMarkdown(TextDescriptor t, string text, string primaryColor)
    {
        var parts = Regex.Split(text, @"(\*\*.*?\*\*|\*.*?\*)");
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
            {
                string inner = part.Substring(2, part.Length - 4);
                t.Span(inner).Bold();
            }
            else if (part.StartsWith("*") && part.EndsWith("*") && part.Length > 2)
            {
                string inner = part.Substring(1, part.Length - 2);
                t.Span(inner).Italic();
            }
            else
            {
                t.Span(part);
            }
        }
    }

    private static string ParseColorOrDefault(string? hex, string defaultHex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return defaultHex;
        string cleaned = hex.Trim();
        if (!cleaned.StartsWith("#")) cleaned = "#" + cleaned;
        return cleaned;
    }
}
