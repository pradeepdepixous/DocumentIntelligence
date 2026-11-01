using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Services;

public class TemplateInspectionResult
{
    public bool IsValid { get; set; } = true;
    public DocumentTemplateDefinition Template { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int ContentControlsFound { get; set; }
    public int TextPlaceholdersFound { get; set; }
}

public interface IDocxTemplateParser
{
    TemplateInspectionResult ParseAndInspect(
        Stream docxStream, 
        string templateId, 
        string? title = null, 
        string? description = null, 
        string? documentType = null);
}

public class DocxTemplateParser : IDocxTemplateParser
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{([a-zA-Z0-9_\.\:\#\/]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex MalformedPlaceholderRegex = new(@"\{[a-zA-Z0-9_]+(?!\})|\b[a-zA-Z0-9_]+\}\}", RegexOptions.Compiled);

    public TemplateInspectionResult ParseAndInspect(
        Stream docxStream, 
        string templateId, 
        string? title = null, 
        string? description = null, 
        string? documentType = null)
    {
        var result = new TemplateInspectionResult();
        var sections = new List<TemplateSectionDefinition>();
        var seenSectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = WordprocessingDocument.Open(docxStream, false);
            var mainPart = doc.MainDocumentPart;
            if (mainPart?.Document?.Body == null)
            {
                result.IsValid = false;
                result.Errors.Add("The uploaded document is invalid or does not contain a main body.");
                return result;
            }

            var body = mainPart.Document.Body;

            // 1. Inspect Word Content Controls (SDT)
            var sdtElements = body.Descendants<SdtElement>().ToList();
            result.ContentControlsFound = sdtElements.Count;

            foreach (var sdt in sdtElements)
            {
                string? tag = sdt.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value;
                string? alias = sdt.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value;
                string fieldId = (tag ?? alias)?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(fieldId) || seenSectionIds.Contains(fieldId))
                {
                    continue;
                }

                // Determine if this SDT is or contains a Table
                var innerTable = sdt.Descendants<Table>().FirstOrDefault();
                if (innerTable != null || sdt is SdtRow || fieldId.StartsWith("table:", StringComparison.OrdinalIgnoreCase))
                {
                    string tableId = fieldId.StartsWith("table:", StringComparison.OrdinalIgnoreCase) 
                        ? fieldId.Substring(6) 
                        : fieldId;

                    var tableColumns = new List<string>();
                    var childSdts = sdt.Descendants<SdtElement>().Where(c => c != sdt).ToList();
                    foreach (var child in childSdts)
                    {
                        string? childTag = child.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value 
                                           ?? child.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value;
                        if (!string.IsNullOrWhiteSpace(childTag) && !tableColumns.Contains(childTag))
                        {
                            tableColumns.Add(childTag);
                        }
                    }

                    sections.Add(new TemplateSectionDefinition
                    {
                        Id = tableId,
                        Title = FormatTitle(alias ?? tableId),
                        Type = "table",
                        Required = true,
                        Description = $"Table requiring columns: {string.Join(", ", tableColumns)}",
                        Fields = tableColumns.Count > 0 ? tableColumns : null
                    });
                    seenSectionIds.Add(tableId);
                    continue;
                }

                // Determine type based on actual XML structural evidence
                int pCount = sdt.Descendants<Paragraph>().Count();
                bool hasBullets = sdt.Descendants<NumberingProperties>().Any();

                string inferredType = "text";
                if (pCount > 1)
                {
                    inferredType = "paragraphs";
                }
                else if (hasBullets)
                {
                    inferredType = "bullet_list";
                }

                sections.Add(new TemplateSectionDefinition
                {
                    Id = fieldId,
                    Title = FormatTitle(alias ?? fieldId),
                    Type = inferredType,
                    Required = true,
                    Description = alias ?? $"Content field '{fieldId}'"
                });
                seenSectionIds.Add(fieldId);
            }

            // 2. Scan text placeholders {{field}} outside of table cells
            var paragraphs = body.Descendants<Paragraph>()
                .Where(p => !p.Ancestors<TableCell>().Any())
                .ToList();

            foreach (var p in paragraphs)
            {
                string text = p.InnerText;
                var matches = PlaceholderRegex.Matches(text);
                foreach (Match m in matches)
                {
                    result.TextPlaceholdersFound++;
                    string rawTag = m.Groups[1].Value.Trim();

                    // Check for explicit table tag prefix like {{table:requirements.req_id}} or {{#requirements}}
                    string fieldId = rawTag.TrimStart('#', '/');
                    if (fieldId.StartsWith("table:", StringComparison.OrdinalIgnoreCase))
                    {
                        fieldId = fieldId.Substring(6);
                    }

                    if (fieldId.Contains('.'))
                    {
                        var parts = fieldId.Split('.');
                        string parentTable = parts[0];
                        string colName = parts[1];

                        var existingTable = sections.FirstOrDefault(s => s.Id.Equals(parentTable, StringComparison.OrdinalIgnoreCase));
                        if (existingTable == null)
                        {
                            existingTable = new TemplateSectionDefinition
                            {
                                Id = parentTable,
                                Title = FormatTitle(parentTable),
                                Type = "table",
                                Required = true,
                                Description = $"Dynamic table for {parentTable}",
                                Fields = new List<string>()
                            };
                            sections.Add(existingTable);
                            seenSectionIds.Add(parentTable);
                        }
                        if (existingTable.Fields != null && !existingTable.Fields.Contains(colName))
                        {
                            existingTable.Fields.Add(colName);
                        }
                        continue;
                    }

                    if (!seenSectionIds.Contains(fieldId))
                    {
                        // Structural check: does the paragraph contain multiple breaks or bullet markers?
                        bool hasBullet = p.ParagraphProperties?.NumberingProperties != null;
                        string inferredType = hasBullet ? "bullet_list" : "text";

                        sections.Add(new TemplateSectionDefinition
                        {
                            Id = fieldId,
                            Title = FormatTitle(fieldId),
                            Type = inferredType,
                            Required = true,
                            Description = $"Content for '{FormatTitle(fieldId)}'"
                        });
                        seenSectionIds.Add(fieldId);
                    }
                }

                // Check for malformed tags (e.g. {tag missing closing brace)
                var malformed = MalformedPlaceholderRegex.Matches(text);
                foreach (Match mf in malformed)
                {
                    result.Warnings.Add($"Potentially unclosed or malformed placeholder detected: '{mf.Value}'");
                }
            }

            // 3. Scan Tables for Repeating Row Placeholders
            var tables = body.Descendants<Table>().ToList();
            foreach (var tbl in tables)
            {
                var rows = tbl.Descendants<TableRow>().ToList();
                if (rows.Count < 2) continue;

                // Look for preceding heading right before the table
                string? precedingHeading = null;
                var prevElem = tbl.PreviousSibling();
                while (prevElem != null)
                {
                    if (prevElem is Paragraph prevP && !string.IsNullOrWhiteSpace(prevP.InnerText))
                    {
                        string trimmed = prevP.InnerText.Trim();
                        var cleaned = Regex.Replace(trimmed, @"^\d+(\.\d+)*\s*", "").Trim();
                        if (!string.IsNullOrWhiteSpace(cleaned))
                        {
                            precedingHeading = cleaned;
                            break;
                        }
                    }
                    prevElem = prevElem.PreviousSibling();
                }

                // Check if row 0 has header cell names
                var headerCols = rows[0].Descendants<TableCell>().Select(c => c.InnerText.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();

                // Look at subsequent data rows for placeholders
                foreach (var row in rows.Skip(1))
                {
                    var cellPlaceholders = new List<string>();
                    foreach (var cell in row.Descendants<TableCell>())
                    {
                        var matches = PlaceholderRegex.Matches(cell.InnerText);
                        foreach (Match m in matches)
                        {
                            cellPlaceholders.Add(m.Groups[1].Value.Trim());
                        }
                    }

                    if (cellPlaceholders.Count >= 1)
                    {
                        string inferredTableName = !string.IsNullOrWhiteSpace(precedingHeading)
                            ? Regex.Replace(precedingHeading.ToLowerInvariant(), @"[^a-z0-9_]+", "_").Trim('_')
                            : (headerCols.Count > 0 ? headerCols[0].ToLowerInvariant().Replace(" ", "_") + "_table" : cellPlaceholders[0].ToLowerInvariant().Replace(" ", "_") + "_table");

                        if (!seenSectionIds.Contains(inferredTableName))
                        {
                            sections.Add(new TemplateSectionDefinition
                            {
                                Id = inferredTableName,
                                Title = FormatTitle(precedingHeading ?? inferredTableName),
                                Type = "table",
                                Required = true,
                                Description = $"Table with columns: {string.Join(", ", cellPlaceholders)}",
                                Fields = cellPlaceholders
                            });
                            seenSectionIds.Add(inferredTableName);
                        }
                    }
                }
            }

            if (sections.Count == 0)
            {
                result.Warnings.Add("No Content Controls or {{placeholder}} tags were found in the uploaded Word document. You may need to add Content Controls or double-curly tags to this template.");
            }

            // 4. Extract real Page Setup & Margins from document's SectionProperties (Preserve original Word formatting)
            var sectPr = body.Descendants<SectionProperties>().FirstOrDefault();
            var presentation = new TemplatePresentation
            {
                ReferenceDocx = $"{templateId}.docx"
            };

            if (sectPr != null)
            {
                var pgSz = sectPr.GetFirstChild<PageSize>();
                if (pgSz?.Width != null && pgSz?.Height != null)
                {
                    // A4 width is 11906 dxa; Letter is 12240 dxa
                    presentation.PageSize = pgSz.Width.Value > 12000 ? "Letter" : "A4";
                }

                var pgMar = sectPr.GetFirstChild<PageMargin>();
                if (pgMar != null)
                {
                    // 1 mm = approx 56.7 dxa
                    if (pgMar.Top?.Value != null) presentation.Margins.Top = pgMar.Top.Value / 56.7f;
                    if (pgMar.Bottom?.Value != null) presentation.Margins.Bottom = pgMar.Bottom.Value / 56.7f;
                    if (pgMar.Left?.Value != null) presentation.Margins.Left = pgMar.Left.Value / 56.7f;
                    if (pgMar.Right?.Value != null) presentation.Margins.Right = pgMar.Right.Value / 56.7f;
                }
            }

            result.Template = new DocumentTemplateDefinition
            {
                TemplateId = templateId,
                Version = 1,
                DocumentType = !string.IsNullOrWhiteSpace(documentType) ? documentType.Trim().ToLowerInvariant() : "document",
                Title = title ?? FormatTitle(templateId),
                Description = description ?? $"Template with {sections.Count} detected fields.",
                ContentSchema = new TemplateContentSchema
                {
                    Sections = sections
                },
                Presentation = presentation
            };
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Failed to parse Word document: {ex.Message}");
        }

        return result;
    }

    private static string FormatTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var words = raw.Replace("_", " ").Replace("-", " ").Split(' ');
        return string.Join(" ", words.Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : "") : ""));
    }
}
