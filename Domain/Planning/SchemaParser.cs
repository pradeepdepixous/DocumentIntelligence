using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixousDocIntelligence.Domain.Planning;

/// <summary>
/// Robust parser that converts user-supplied JSON schemas into an immutable ExtractionPlan.
/// Supports:
/// 1. Native Pixous ExtractionPlan format (with 'desired_fields')
/// 2. Array of field definition objects
/// 3. Standard JSON Schema (Draft-07, OpenAPI) with 'properties' and 'required'
/// 4. Lightweight key-value dictionary (e.g. {"invoice_number": "string", "total": "number"})
/// </summary>
public static class SchemaParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static ExtractionPlan Parse(string schemaJson, string? defaultUserInstruction = null)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            throw new ArgumentException("Schema JSON cannot be null or empty.", nameof(schemaJson));
        }

        string cleanJson = schemaJson.Trim();
        if ((cleanJson.StartsWith("\"") && cleanJson.EndsWith("\"")) || (cleanJson.StartsWith("'") && cleanJson.EndsWith("'")))
        {
            cleanJson = cleanJson[1..^1].Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r");
        }

        var docOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        using var doc = JsonDocument.Parse(cleanJson, docOptions);
        var root = doc.RootElement;

        // 1. Array of field objects
        if (root.ValueKind == JsonValueKind.Array)
        {
            return ParseFieldArray(root, defaultUserInstruction);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Invalid schema JSON. Root must be a JSON object or array, found {root.ValueKind}.");
        }

        // 2. Native ExtractionPlan with 'desired_fields'
        if (root.TryGetProperty("desired_fields", out var desiredFieldsElem) || 
            root.TryGetProperty("desiredFields", out desiredFieldsElem))
        {
            var plan = JsonSerializer.Deserialize<ExtractionPlan>(schemaJson, JsonOptions) ?? new ExtractionPlan();
            if (string.IsNullOrWhiteSpace(plan.UserInstruction) && !string.IsNullOrWhiteSpace(defaultUserInstruction))
            {
                plan.UserInstruction = defaultUserInstruction;
            }
            plan.ExtractionMode = "schema";
            return plan;
        }

        // 3. Standard JSON Schema with 'properties'
        if (root.TryGetProperty("properties", out var propertiesElem) && propertiesElem.ValueKind == JsonValueKind.Object)
        {
            return ParseStandardJsonSchema(root, propertiesElem, defaultUserInstruction);
        }

        // 4. Simple Key-Value dictionary (e.g. { "contract_title": "string", "parties": "array" })
        return ParseKeyValueDictionary(root, defaultUserInstruction);
    }

    private static ExtractionPlan ParseFieldArray(JsonElement arrayElem, string? defaultUserInstruction)
    {
        var plan = new ExtractionPlan
        {
            ExtractionMode = "schema",
            TargetDocumentType = "Custom Schema Document",
            UserInstruction = defaultUserInstruction ?? "Extract fields defined by manual JSON schema."
        };

        foreach (var item in arrayElem.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                string key = GetStringProp(item, "field_key", "key", "name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) continue;

                string label = GetStringProp(item, "field_label", "label", "title") ?? HumanizeKey(key);
                string type = NormalizeType(GetStringProp(item, "value_type", "type") ?? "string");
                string desc = GetStringProp(item, "description", "desc") ?? $"The {label} of the document.";
                bool mandatory = GetBoolProp(item, "is_mandatory", "mandatory", "required") ?? true;

                plan.DesiredFields.Add(new DynamicFieldDefinition
                {
                    FieldKey = key,
                    FieldLabel = label,
                    ValueType = type,
                    Description = desc,
                    IsMandatory = mandatory
                });
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                string key = item.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    plan.DesiredFields.Add(new DynamicFieldDefinition
                    {
                        FieldKey = key,
                        FieldLabel = HumanizeKey(key),
                        ValueType = InferTypeFromKey(key),
                        Description = $"The {HumanizeKey(key)} of the document.",
                        IsMandatory = true
                    });
                }
            }
        }

        return plan;
    }

    private static ExtractionPlan ParseStandardJsonSchema(JsonElement root, JsonElement propertiesElem, string? defaultUserInstruction)
    {
        string title = GetStringProp(root, "title", "description") ?? "Structured Document";

        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("required", out var reqElem) && reqElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reqElem.EnumerateArray())
            {
                if (r.ValueKind == JsonValueKind.String)
                {
                    var reqKey = r.GetString();
                    if (!string.IsNullOrWhiteSpace(reqKey)) requiredSet.Add(reqKey);
                }
            }
        }

        var plan = new ExtractionPlan
        {
            ExtractionMode = "schema",
            TargetDocumentType = title,
            UserInstruction = defaultUserInstruction ?? $"Extract structured fields according to the {title} schema."
        };

        foreach (var prop in propertiesElem.EnumerateObject())
        {
            string key = prop.Name;
            var propVal = prop.Value;

            string label = GetStringProp(propVal, "title") ?? HumanizeKey(key);
            string rawType = GetStringProp(propVal, "type") ?? "string";
            string? format = GetStringProp(propVal, "format");
            string type = NormalizeType(rawType, format);
            string desc = GetStringProp(propVal, "description") ?? $"The {label} of the document.";
            bool isMandatory = requiredSet.Contains(key);

            var field = new DynamicFieldDefinition
            {
                FieldKey = key,
                FieldLabel = label,
                ValueType = type,
                Description = desc,
                IsMandatory = isMandatory
            };

            // Check if aliases or examples provided
            if (propVal.TryGetProperty("examples", out var examplesElem) && examplesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var ex in examplesElem.EnumerateArray())
                {
                    if (ex.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ex.GetString()))
                    {
                        field.Aliases.Add(ex.GetString()!);
                    }
                }
            }

            plan.DesiredFields.Add(field);
        }

        return plan;
    }

    private static ExtractionPlan ParseKeyValueDictionary(JsonElement root, string? defaultUserInstruction)
    {
        var plan = new ExtractionPlan
        {
            ExtractionMode = "schema",
            TargetDocumentType = "Custom Document",
            UserInstruction = defaultUserInstruction ?? "Extract fields defined by manual JSON schema."
        };

        foreach (var prop in root.EnumerateObject())
        {
            string key = prop.Name;
            string label = HumanizeKey(key);
            string desc;
            string type;

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                string val = prop.Value.GetString()?.Trim() ?? string.Empty;
                if (IsRecognizedType(val))
                {
                    type = NormalizeType(val);
                    desc = $"The {label} of the document.";
                }
                else
                {
                    type = InferTypeFromKey(key);
                    desc = val;
                }
            }
            else
            {
                type = InferTypeFromKey(key);
                desc = $"The {label} of the document.";
            }

            plan.DesiredFields.Add(new DynamicFieldDefinition
            {
                FieldKey = key,
                FieldLabel = label,
                ValueType = type,
                Description = desc,
                IsMandatory = true
            });
        }

        return plan;
    }

    private static string? GetStringProp(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private static bool? GetBoolProp(JsonElement elem, params string[] names)
    {
        foreach (var name in names)
        {
            if (elem.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
        }
        return null;
    }

    private static string NormalizeType(string rawType, string? format = null)
    {
        string lower = rawType.ToLowerInvariant();
        if (format != null && (format.Equals("date", StringComparison.OrdinalIgnoreCase) || format.Equals("date-time", StringComparison.OrdinalIgnoreCase)))
        {
            return "date";
        }

        return lower switch
        {
            "string" => "string",
            "number" or "integer" or "float" or "decimal" => "number",
            "date" or "datetime" or "timestamp" => "date",
            "currency" or "money" or "amount" => "currency",
            "array" or "list" => "array",
            "boolean" or "bool" => "boolean",
            _ => "string"
        };
    }

    private static bool IsRecognizedType(string value)
    {
        string lower = value.ToLowerInvariant();
        return lower is "string" or "number" or "integer" or "float" or "decimal" or "date" or "datetime" or "currency" or "array" or "list" or "boolean" or "bool";
    }

    private static string InferTypeFromKey(string key)
    {
        string lower = key.ToLowerInvariant();
        if (lower.Contains("date") || lower.EndsWith("_at") || lower.EndsWith("_on")) return "date";
        if (lower.Contains("amount") || lower.Contains("price") || lower.Contains("total") || lower.Contains("fee") || lower.Contains("cost")) return "currency";
        if (lower.Contains("count") || lower.Contains("quantity") || lower.Contains("number") || lower.Contains("rate") || lower.Contains("percentage")) return "number";
        if (lower.Contains("parties") || lower.Contains("items") || lower.Contains("signatories") || lower.EndsWith("list") || lower.EndsWith("tags")) return "array";
        if (lower.StartsWith("is_") || lower.StartsWith("has_")) return "boolean";
        return "string";
    }

    private static string HumanizeKey(string key)
    {
        string replaced = key.Replace('_', ' ').Replace('-', ' ');
        return Regex.Replace(replaced, @"\b([a-z])", m => m.Value.ToUpperInvariant());
    }
}
