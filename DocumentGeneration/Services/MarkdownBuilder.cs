using System.Text;
using System.Text.Json;
using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Services;

public interface IMarkdownBuilder
{
    string BuildMarkdown(DocumentTemplateDefinition template, Dictionary<string, JsonElement> content);
}

public class MarkdownBuilder : IMarkdownBuilder
{
    public string BuildMarkdown(DocumentTemplateDefinition template, Dictionary<string, JsonElement> content)
    {
        var sb = new StringBuilder();
        BuildGenericDocumentMarkdown(sb, template, content);
        return sb.ToString().Trim();
    }



    private void BuildGenericDocumentMarkdown(StringBuilder sb, DocumentTemplateDefinition template, Dictionary<string, JsonElement> content)
    {
        if (!string.IsNullOrWhiteSpace(template.Title))
        {
            sb.AppendLine($"# {template.Title}");
            sb.AppendLine();
        }

        foreach (var section in template.ContentSchema.Sections)
        {
            if (!content.TryGetValue(section.Id, out var element)) continue;

            string heading = !string.IsNullOrWhiteSpace(section.Title) 
                ? section.Title 
                : (!string.IsNullOrWhiteSpace(section.Description) && section.Description.Length <= 40 ? section.Description : FormatColumnName(section.Id));

            sb.AppendLine($"## {heading}");
            AppendSectionElement(sb, element);
            sb.AppendLine();
        }
    }

    private static void AppendBodyContent(StringBuilder sb, JsonElement bodyEl)
    {
        if (bodyEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var para in bodyEl.EnumerateArray())
            {
                if (para.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine(para.GetString());
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine(para.ToString());
                    sb.AppendLine();
                }
            }
        }
        else if (bodyEl.ValueKind == JsonValueKind.String)
        {
            sb.AppendLine(bodyEl.GetString());
            sb.AppendLine();
        }
    }

    private static void AppendSectionElement(StringBuilder sb, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                var items = element.EnumerateArray().ToList();
                if (items.Count > 0 && items[0].ValueKind == JsonValueKind.Object)
                {
                    AppendMarkdownTable(sb, items);
                }
                else
                {
                    foreach (var item in items)
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            sb.AppendLine($"- {item.GetString()}");
                        }
                        else
                        {
                            sb.AppendLine($"- {item.ToString()}");
                        }
                    }
                }
                break;

            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var arr = prop.Value.EnumerateArray().ToList();
                        sb.AppendLine($"### {FormatColumnName(prop.Name)}");
                        if (arr.Count > 0 && arr[0].ValueKind == JsonValueKind.Object)
                        {
                            AppendMarkdownTable(sb, arr);
                        }
                        else
                        {
                            foreach (var item in arr)
                            {
                                sb.AppendLine($"- {item.ToString()}");
                            }
                            sb.AppendLine();
                        }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        sb.AppendLine($"### {FormatColumnName(prop.Name)}");
                        AppendSectionElement(sb, prop.Value);
                    }
                    else
                    {
                        sb.AppendLine($"**{FormatColumnName(prop.Name)}:** {prop.Value.ToString()}  ");
                    }
                }
                break;

            default:
                sb.AppendLine(element.ToString());
                break;
        }
    }

    private static string? GetValueAsString(Dictionary<string, JsonElement> content, string key)
    {
        if (content.TryGetValue(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.String) return el.GetString();
            if (el.ValueKind != JsonValueKind.Null && el.ValueKind != JsonValueKind.Undefined) return el.ToString();
        }
        return null;
    }

    private static void AppendMarkdownTable(StringBuilder sb, List<JsonElement> rows)
    {
        if (rows.Count == 0) return;

        // Gather all distinct column names from all rows in order
        var columns = new List<string>();
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in row.EnumerateObject())
            {
                if (!columns.Contains(prop.Name)) columns.Add(prop.Name);
            }
        }

        if (columns.Count == 0) return;

        // Header line
        sb.AppendLine("| " + string.Join(" | ", columns.Select(FormatColumnName)) + " |");
        // Separator line
        sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

        // Data rows
        foreach (var row in rows)
        {
            var cellValues = new List<string>();
            foreach (var col in columns)
            {
                string cellText = "";
                if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty(col, out var val))
                {
                    cellText = val.ValueKind == JsonValueKind.String 
                        ? val.GetString() ?? "" 
                        : val.ToString();
                    cellText = cellText.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
                }
                cellValues.Add(cellText);
            }
            sb.AppendLine("| " + string.Join(" | ", cellValues) + " |");
        }
        sb.AppendLine();
    }

    private static string FormatColumnName(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        var words = key.Replace("_", " ").Split(' ');
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : "")));
    }
}
