using System.Collections.Concurrent;
using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Repositories;

public interface ITemplateRepository
{
    Task<DocumentTemplateDefinition?> GetTemplateAsync(string templateId, int? version = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentTemplateDefinition>> ListTemplatesAsync(CancellationToken cancellationToken = default);
    Task SaveTemplateAsync(DocumentTemplateDefinition template, CancellationToken cancellationToken = default);
    Task SaveTemplateDocxAsync(string templateId, byte[] docxBytes, CancellationToken cancellationToken = default);
    Task<byte[]?> GetTemplateDocxAsync(string templateId, CancellationToken cancellationToken = default);
    Task<bool> HasTemplateDocxAsync(string templateId, CancellationToken cancellationToken = default);
}

public class TemplateRepository : ITemplateRepository
{
    private readonly ConcurrentDictionary<string, DocumentTemplateDefinition> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _templateDir;

    public TemplateRepository()
    {
        _templateDir = Path.Combine(Directory.GetCurrentDirectory(), "output", "templates");
        Directory.CreateDirectory(_templateDir);
        LoadPersistedTemplates();
        SeedDefaultTemplates();
    }

    public Task<DocumentTemplateDefinition?> GetTemplateAsync(string templateId, int? version = null, CancellationToken cancellationToken = default)
    {
        _templates.TryGetValue(templateId, out var template);
        return Task.FromResult(template);
    }

    public Task<IEnumerable<DocumentTemplateDefinition>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<DocumentTemplateDefinition>>(_templates.Values.ToList());
    }

    public async Task SaveTemplateAsync(DocumentTemplateDefinition template, CancellationToken cancellationToken = default)
    {
        _templates[template.TemplateId] = template;
        string metaPath = Path.Combine(_templateDir, $"{template.TemplateId}.json");
        string json = System.Text.Json.JsonSerializer.Serialize(template, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metaPath, json, cancellationToken);
    }

    public async Task SaveTemplateDocxAsync(string templateId, byte[] docxBytes, CancellationToken cancellationToken = default)
    {
        string filePath = Path.Combine(_templateDir, $"{templateId}.docx");
        await File.WriteAllBytesAsync(filePath, docxBytes, cancellationToken);
    }

    public async Task<byte[]?> GetTemplateDocxAsync(string templateId, CancellationToken cancellationToken = default)
    {
        string filePath = Path.Combine(_templateDir, $"{templateId}.docx");
        if (File.Exists(filePath))
        {
            return await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        return null;
    }

    public Task<bool> HasTemplateDocxAsync(string templateId, CancellationToken cancellationToken = default)
    {
        string filePath = Path.Combine(_templateDir, $"{templateId}.docx");
        return Task.FromResult(File.Exists(filePath));
    }

    private void LoadPersistedTemplates()
    {
        try
        {
            var jsonFiles = Directory.GetFiles(_templateDir, "*.json");
            foreach (var file in jsonFiles)
            {
                string json = File.ReadAllText(file);
                var template = System.Text.Json.JsonSerializer.Deserialize<DocumentTemplateDefinition>(json);
                if (template != null && !string.IsNullOrWhiteSpace(template.TemplateId))
                {
                    _templates[template.TemplateId] = template;
                }
            }
        }
        catch
        {
            // Ignore load errors for corrupt files
        }
    }

    private void SeedDefaultTemplates()
    {
        var officialLetter = new DocumentTemplateDefinition
        {
            TemplateId = "official_letter",
            Version = 1,
            DocumentType = "letter",
            Title = "Official Business Letter",
            Description = "Standard enterprise template for formal requests, administrative notices, and official correspondence.",
            ContentSchema = new TemplateContentSchema
            {
                Sections = new List<TemplateSectionDefinition>
                {
                    new() { Id = "recipient", Type = "address_block", Required = true, Description = "Recipient title and official office address" },
                    new() { Id = "date", Type = "date", Required = true, Description = "Document date" },
                    new() { Id = "subject", Type = "text", Required = true, Description = "Formal subject line" },
                    new() { Id = "body", Type = "paragraphs", Required = true, Description = "Main body narrative paragraphs" },
                    new() { Id = "closing", Type = "text", Required = true, Description = "Formal closing sign-off" },
                    new() { Id = "signature", Type = "signature_block", Required = true, Description = "Signatory full name and official title/designation" }
                }
            },
            Presentation = new TemplatePresentation
            {
                ReferenceDocx = "official_letter_v1.docx",
                PageSize = "A4",
                FontFamily = "Arial",
                PrimaryColor = "#1E293B"
            }
        };

        _templates[officialLetter.TemplateId] = officialLetter;

        var brdTemplate = new DocumentTemplateDefinition
        {
            TemplateId = "brd",
            Version = 1,
            DocumentType = "brd",
            Title = "Business Requirements Document (BRD)",
            Description = "Comprehensive enterprise specification document defining project overview, objectives, functional & non-functional requirements, stakeholders, and risks.",
            ContentSchema = new TemplateContentSchema
            {
                Sections = new List<TemplateSectionDefinition>
                {
                    new()
                    {
                        Id = "project_overview",
                        Title = "1. Project Overview",
                        Type = "object",
                        Required = true,
                        Description = "Project metadata: project_name, document_version, date, author, status",
                        Fields = new() { "project_name", "document_version", "date", "author", "status" }
                    },
                    new()
                    {
                        Id = "executive_summary",
                        Title = "2. Executive Summary",
                        Type = "paragraphs",
                        Required = true,
                        Description = "Executive narrative covering business background, problem statement, and proposed solution vision"
                    },
                    new()
                    {
                        Id = "business_objectives",
                        Title = "3. Business Objectives",
                        Type = "bullet_list",
                        Required = true,
                        Description = "Key measurable business goals and strategic outcomes"
                    },
                    new()
                    {
                        Id = "project_scope",
                        Title = "4. Project Scope",
                        Type = "object",
                        Required = true,
                        Description = "In-scope capabilities and out-of-scope boundaries",
                        Fields = new() { "in_scope", "out_of_scope" }
                    },
                    new()
                    {
                        Id = "stakeholders",
                        Title = "5. Stakeholder Analysis",
                        Type = "table",
                        Required = true,
                        Description = "Identified stakeholders, roles, and project responsibilities",
                        Fields = new() { "role", "stakeholder_name", "department", "responsibility" }
                    },
                    new()
                    {
                        Id = "functional_requirements",
                        Title = "6. Functional Requirements",
                        Type = "table",
                        Required = true,
                        Description = "Detailed functional requirements matrix",
                        Fields = new() { "req_id", "module", "requirement_description", "priority", "acceptance_criteria" }
                    },
                    new()
                    {
                        Id = "non_functional_requirements",
                        Title = "7. Non-Functional Requirements",
                        Type = "table",
                        Required = false,
                        Description = "Performance, security, compliance, and availability standards",
                        Fields = new() { "category", "requirement", "target_metric" }
                    },
                    new()
                    {
                        Id = "risks_and_mitigations",
                        Title = "8. Risks & Mitigations",
                        Type = "table",
                        Required = false,
                        Description = "Identified project risks, severity, and mitigation plans",
                        Fields = new() { "risk_id", "risk_description", "impact", "mitigation_strategy" }
                    },
                    new()
                    {
                        Id = "approvals",
                        Title = "9. Review & Sign-Off",
                        Type = "table",
                        Required = false,
                        Description = "Approval matrix for key decision makers",
                        Fields = new() { "stakeholder_name", "role", "approval_status", "date" }
                    }
                }
            },
            Presentation = new TemplatePresentation
            {
                ReferenceDocx = "brd_v1.docx",
                PageSize = "A4",
                FontFamily = "Arial",
                PrimaryColor = "#0F172A",
                Margins = new PageMargins { Top = 20, Bottom = 20, Left = 20, Right = 20 }
            }
        };

        _templates[brdTemplate.TemplateId] = brdTemplate;
    }
}
