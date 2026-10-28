using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Rendering;

public interface IDocumentRenderer
{
    string Format { get; }
    byte[] Render(DocumentTemplateDefinition template, Dictionary<string, System.Text.Json.JsonElement> content, string markdown);
}
