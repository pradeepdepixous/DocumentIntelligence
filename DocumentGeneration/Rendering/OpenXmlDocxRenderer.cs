using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Rendering;

public class OpenXmlDocxRenderer : IDocumentRenderer
{
    public string Format => "docx";

    public byte[] Render(DocumentTemplateDefinition template, Dictionary<string, JsonElement> content, string markdown)
    {
        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            string font = template.Presentation?.FontFamily ?? "Arial";
            string primaryColor = NormalizeHexColor(template.Presentation?.PrimaryColor ?? "1E293B");

            // Render Markdown lines into Wordprocessing paragraphs
            RenderMarkdownToBody(body, markdown, font, primaryColor);

            // Configure Page Setup (A4 or Letter, Margins)
            var sectionProps = new SectionProperties();
            var pageSize = new PageSize
            {
                Width = (uint)(template.Presentation?.PageSize?.Equals("Letter", StringComparison.OrdinalIgnoreCase) == true ? 12240 : 11906), // A4: 210mm = 11906 dxa
                Height = (uint)(template.Presentation?.PageSize?.Equals("Letter", StringComparison.OrdinalIgnoreCase) == true ? 15840 : 16838) // A4: 297mm = 16838 dxa
            };
            sectionProps.AppendChild(pageSize);

            float topMm = template.Presentation?.Margins?.Top ?? 20f;
            float botMm = template.Presentation?.Margins?.Bottom ?? 20f;
            float leftMm = template.Presentation?.Margins?.Left ?? 20f;
            float rightMm = template.Presentation?.Margins?.Right ?? 20f;

            // 1 mm = 56.692913 dxa (approx 57 dxa)
            var pageMargin = new PageMargin
            {
                Top = (int)(topMm * 56.7f),
                Bottom = (int)(botMm * 56.7f),
                Left = (uint)(leftMm * 56.7f),
                Right = (uint)(rightMm * 56.7f)
            };
            sectionProps.AppendChild(pageMargin);
            body.AppendChild(sectionProps);

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static void RenderMarkdownToBody(Body body, string markdown, string font, string primaryColor)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            string line = lines[i].TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" })));
                i++;
                continue;
            }

            // Check for Markdown Table (consecutive lines starting with '|')
            if (line.TrimStart().StartsWith("|"))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }
                AppendOpenXmlTable(body, tableLines, font, primaryColor);
                continue;
            }

            if (line.StartsWith("# "))
            {
                string headingText = line.Substring(2).Trim();
                var p = new Paragraph(new ParagraphProperties(
                    new SpacingBetweenLines { Before = "240", After = "120" }
                ));
                p.AppendChild(CreateRun(headingText, font, "28", isBold: true, color: primaryColor));
                body.AppendChild(p);
            }
            else if (line.StartsWith("## "))
            {
                string headingText = line.Substring(3).Trim();
                var p = new Paragraph(new ParagraphProperties(
                    new SpacingBetweenLines { Before = "200", After = "100" }
                ));
                p.AppendChild(CreateRun(headingText, font, "24", isBold: true, color: primaryColor));
                body.AppendChild(p);
            }
            else if (line.StartsWith("### "))
            {
                string headingText = line.Substring(4).Trim();
                var p = new Paragraph(new ParagraphProperties(
                    new SpacingBetweenLines { Before = "160", After = "80" }
                ));
                p.AppendChild(CreateRun(headingText, font, "22", isBold: true, color: primaryColor));
                body.AppendChild(p);
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                string itemText = line.Substring(2).Trim();
                var p = new Paragraph(new ParagraphProperties(
                    new Indentation { Left = "400" },
                    new SpacingBetweenLines { After = "60" }
                ));
                p.AppendChild(CreateRun("• ", font, "22", isBold: false, color: primaryColor));
                AppendFormattedRuns(p, itemText, font, "22", "334155");
                body.AppendChild(p);
            }
            else
            {
                var p = new Paragraph(new ParagraphProperties(
                    new SpacingBetweenLines { After = "100", Line = "260", LineRule = LineSpacingRuleValues.Auto }
                ));
                AppendFormattedRuns(p, line, font, "22", "334155");
                body.AppendChild(p);
            }

            i++;
        }
    }

    private static void AppendOpenXmlTable(Body body, List<string> tableLines, string font, string primaryColor)
    {
        if (tableLines.Count == 0) return;

        var table = new Table();
        var tblProps = new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CBD5E1" },
                new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "94A3B8" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }
            ),
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" } // 100%
        );
        table.AppendChild(tblProps);

        bool isHeader = true;
        foreach (var rawLine in tableLines)
        {
            string trimmed = rawLine.Trim();
            if (trimmed.StartsWith("|") && trimmed.EndsWith("|") && trimmed.Length >= 2)
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }
            var cells = trimmed.Split('|').Select(c => c.Trim()).ToList();

            // Skip separator line (|---|---|)
            if (cells.All(c => c.All(ch => ch == '-' || ch == ':' || char.IsWhiteSpace(ch))))
            {
                isHeader = false;
                continue;
            }

            var row = new TableRow();
            if (isHeader)
            {
                row.AppendChild(new TableRowProperties(new TableHeader()));
            }

            int cellWidthPct = cells.Count > 0 ? 5000 / cells.Count : 1000;
            foreach (var cellText in cells)
            {
                var cell = new TableCell();
                var cellProps = new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = cellWidthPct.ToString() },
                    new TableCellMargin(new TopMargin { Width = "100" }, new BottomMargin { Width = "100" }, new LeftMargin { Width = "150" }, new RightMargin { Width = "150" })
                );

                if (isHeader)
                {
                    cellProps.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Fill = "F1F5F9" });
                }

                cell.AppendChild(cellProps);
                var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "40" }));
                AppendFormattedRuns(p, cellText, font, isHeader ? "20" : "19", isHeader ? primaryColor : "334155");
                cell.AppendChild(p);
                row.AppendChild(cell);
            }

            table.AppendChild(row);
            isHeader = false;
        }

        body.AppendChild(table);
        body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" })));
    }

    private static void AppendFormattedRuns(Paragraph p, string text, string font, string fontSize, string defaultColor)
    {
        // Simple regex parser for **bold** and plain text segments
        var parts = Regex.Split(text, @"(\*\*.*?\*\*|\*.*?\*)");
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
            {
                string inner = part.Substring(2, part.Length - 4);
                p.AppendChild(CreateRun(inner, font, fontSize, isBold: true, color: defaultColor));
            }
            else if (part.StartsWith("*") && part.EndsWith("*") && part.Length > 2)
            {
                string inner = part.Substring(1, part.Length - 2);
                p.AppendChild(CreateRun(inner, font, fontSize, isItalic: true, color: defaultColor));
            }
            else
            {
                p.AppendChild(CreateRun(part, font, fontSize, isBold: false, color: defaultColor));
            }
        }
    }

    private static Run CreateRun(string text, string font, string halfPtSize, bool isBold = false, bool isItalic = false, string? color = null)
    {
        var rProps = new RunProperties(
            new RunFonts { Ascii = font, HighAnsi = font },
            new FontSize { Val = halfPtSize }
        );

        if (isBold) rProps.AppendChild(new Bold());
        if (isItalic) rProps.AppendChild(new Italic());
        if (!string.IsNullOrEmpty(color)) rProps.AppendChild(new Color { Val = color });

        return new Run(rProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static string NormalizeHexColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "1E293B";
        return hex.TrimStart('#').ToUpperInvariant();
    }
}
