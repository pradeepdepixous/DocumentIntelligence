using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Validation;

public class TemplateValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();

    public static TemplateValidationResult Success() => new() { IsValid = true };
    public static TemplateValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors.ToList() };
}

public interface ITemplateValidator
{
    TemplateValidationResult Validate(DocumentTemplateDefinition template);
}

public class TemplateValidator : ITemplateValidator
{
    private static readonly HashSet<string> SupportedSectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "address_block", "date", "text", "paragraphs", "signature_block", "table", "list", "bullet_list", "heading", "object"
    };

    public TemplateValidationResult Validate(DocumentTemplateDefinition template)
    {
        var errors = new List<string>();

        if (template == null)
        {
            return TemplateValidationResult.Failure("Template definition cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(template.TemplateId))
        {
            errors.Add("Template 'template_id' is required.");
        }

        if (template.Version <= 0)
        {
            errors.Add("Template 'version' must be greater than 0.");
        }

        if (template.ContentSchema?.Sections == null || template.ContentSchema.Sections.Count == 0)
        {
            errors.Add("Template must define at least one section in 'content_schema.sections'.");
        }
        else
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in template.ContentSchema.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Id))
                {
                    errors.Add("Section 'id' cannot be empty.");
                    continue;
                }

                if (!seenIds.Add(section.Id))
                {
                    errors.Add($"Duplicate section id '{section.Id}' found in template schema.");
                }

                if (string.IsNullOrWhiteSpace(section.Type))
                {
                    errors.Add($"Section '{section.Id}' must specify a 'type'.");
                }
                else if (!SupportedSectionTypes.Contains(section.Type))
                {
                    errors.Add($"Section '{section.Id}' specifies an unsupported type '{section.Type}'. Supported types: {string.Join(", ", SupportedSectionTypes)}.");
                }
            }
        }

        return errors.Count == 0 
            ? TemplateValidationResult.Success() 
            : new TemplateValidationResult { IsValid = false, Errors = errors };
    }
}
