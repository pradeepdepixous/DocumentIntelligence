using System.Text.Json;
using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Validation;

public class ContentValidationResult
{
    public bool IsValid { get; set; } = true;
    public string Status { get; set; } = "ready"; // "ready" or "needs_information"
    public List<string> MissingMandatoryFields { get; set; } = new();
    public List<string> MissingOptionalFields { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public interface IContentValidator
{
    ContentValidationResult ValidateContent(
        DocumentTemplateDefinition template, 
        Dictionary<string, JsonElement> content, 
        IEnumerable<string>? llmMissingFields = null);
}

public class ContentValidator : IContentValidator
{
    public ContentValidationResult ValidateContent(
        DocumentTemplateDefinition template, 
        Dictionary<string, JsonElement> content, 
        IEnumerable<string>? llmMissingFields = null)
    {
        var result = new ContentValidationResult();
        var missingMandatory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingOptional = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Include any missing fields explicitly identified by the LLM
        if (llmMissingFields != null)
        {
            foreach (var mf in llmMissingFields)
            {
                if (IsFieldMandatoryInTemplate(template, mf))
                {
                    missingMandatory.Add(mf);
                }
                else
                {
                    missingOptional.Add(mf);
                }
            }
        }

        // Validate each section defined in the template
        foreach (var section in template.ContentSchema.Sections)
        {
            bool hasSection = content.TryGetValue(section.Id, out var sectionElement);
            bool isNullOrUndefined = !hasSection || sectionElement.ValueKind == JsonValueKind.Null || sectionElement.ValueKind == JsonValueKind.Undefined;

            if (isNullOrUndefined)
            {
                if (section.Required)
                {
                    missingMandatory.Add(section.Id);
                }
                else
                {
                    missingOptional.Add(section.Id);
                }
                continue;
            }

            // Type-specific field extraction and validation
            switch (section.Type.ToLowerInvariant())
            {
                case "address_block":
                    ValidateAddressBlock(section, sectionElement, missingMandatory, missingOptional);
                    break;

                case "signature_block":
                    ValidateSignatureBlock(section, sectionElement, missingMandatory, missingOptional);
                    break;

                case "date":
                    if (!string.IsNullOrWhiteSpace(sectionElement.GetString()))
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (section.Required)
                    {
                        missingMandatory.Add(section.Id);
                    }
                    break;

                case "text":
                    if (!string.IsNullOrWhiteSpace(sectionElement.GetString()))
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (section.Required)
                    {
                        missingMandatory.Add(section.Id);
                    }
                    break;

                case "paragraphs":
                    if (sectionElement.ValueKind == JsonValueKind.Array && sectionElement.GetArrayLength() > 0)
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (sectionElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sectionElement.GetString()))
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (section.Required)
                    {
                        missingMandatory.Add(section.Id);
                    }
                    break;

                case "table":
                    if (sectionElement.ValueKind == JsonValueKind.Array && sectionElement.GetArrayLength() > 0)
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (section.Required)
                    {
                        missingMandatory.Add(section.Id);
                    }
                    break;

                case "list":
                case "bullet_list":
                    if (sectionElement.ValueKind == JsonValueKind.Array && sectionElement.GetArrayLength() > 0)
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (sectionElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sectionElement.GetString()))
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (section.Required)
                    {
                        missingMandatory.Add(section.Id);
                    }
                    break;

                case "object":
                    if (sectionElement.ValueKind == JsonValueKind.Object && sectionElement.EnumerateObject().Any())
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (section.Required)
                    {
                        missingMandatory.Add(section.Id);
                    }
                    break;

                default:
                    if (sectionElement.ValueKind != JsonValueKind.Null && sectionElement.ValueKind != JsonValueKind.Undefined)
                    {
                        missingMandatory.Remove(section.Id);
                        missingOptional.Remove(section.Id);
                    }
                    else if (section.Required)
                    {
                        missingMandatory.Add(section.Id);
                    }
                    break;
            }
        }

        result.MissingMandatoryFields = missingMandatory.OrderBy(x => x).ToList();
        result.MissingOptionalFields = missingOptional.OrderBy(x => x).ToList();

        if (result.MissingMandatoryFields.Count > 0)
        {
            result.Status = "needs_information";
            result.IsValid = false;
        }
        else
        {
            result.Status = "ready";
            result.IsValid = true;
        }

        return result;
    }

    private static void ValidateAddressBlock(
        TemplateSectionDefinition section, 
        JsonElement element, 
        HashSet<string> mandatory, 
        HashSet<string> optional)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool hasName = element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nameProp.GetString());
            bool hasAddress = element.TryGetProperty("address", out var addrProp) && addrProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(addrProp.GetString());

            if (!hasName)
            {
                if (section.Required) mandatory.Add($"{section.Id}.name");
                else optional.Add($"{section.Id}.name");
            }
            else
            {
                mandatory.Remove($"{section.Id}.name");
                optional.Remove($"{section.Id}.name");
            }

            if (!hasAddress)
            {
                if (section.Required) mandatory.Add($"{section.Id}.address");
                else optional.Add($"{section.Id}.address");
            }
            else
            {
                mandatory.Remove($"{section.Id}.address");
                optional.Remove($"{section.Id}.address");
            }

            if (hasName && hasAddress)
            {
                mandatory.Remove(section.Id);
                optional.Remove(section.Id);
            }
        }
        else if (string.IsNullOrWhiteSpace(element.GetString()))
        {
            if (section.Required) mandatory.Add(section.Id);
        }
        else
        {
            mandatory.Remove(section.Id);
            optional.Remove(section.Id);
        }
    }

    private static void ValidateSignatureBlock(
        TemplateSectionDefinition section, 
        JsonElement element, 
        HashSet<string> mandatory, 
        HashSet<string> optional)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool hasName = element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nameProp.GetString());
            bool hasDesig = element.TryGetProperty("designation", out var desigProp) && desigProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(desigProp.GetString());

            if (!hasName)
            {
                if (section.Required) mandatory.Add($"{section.Id}.name");
                else optional.Add($"{section.Id}.name");
            }
            else
            {
                mandatory.Remove($"{section.Id}.name");
                optional.Remove($"{section.Id}.name");
            }

            if (!hasDesig)
            {
                if (section.Required) mandatory.Add($"{section.Id}.designation");
                else optional.Add($"{section.Id}.designation");
            }
            else
            {
                mandatory.Remove($"{section.Id}.designation");
                optional.Remove($"{section.Id}.designation");
            }

            if (hasName && hasDesig)
            {
                mandatory.Remove(section.Id);
                optional.Remove(section.Id);
            }
        }
        else if (string.IsNullOrWhiteSpace(element.GetString()))
        {
            if (section.Required) mandatory.Add(section.Id);
        }
        else
        {
            mandatory.Remove(section.Id);
            optional.Remove(section.Id);
        }
    }

    private static bool IsFieldMandatoryInTemplate(DocumentTemplateDefinition template, string fieldPath)
    {
        string rootSectionId = fieldPath.Contains('.') ? fieldPath.Split('.')[0] : fieldPath;
        var section = template.ContentSchema.Sections.FirstOrDefault(s => string.Equals(s.Id, rootSectionId, StringComparison.OrdinalIgnoreCase));
        return section?.Required ?? false;
    }
}
