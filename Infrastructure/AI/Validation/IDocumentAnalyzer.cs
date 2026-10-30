using PixousDocIntelligence.Infrastructure.AI.Validation.Models;

namespace PixousDocIntelligence.Infrastructure.AI.Validation;

public interface IDocumentAnalyzer
{
    Task<GeminiDocumentObservation> AnalyzeAsync(
        byte[] fileBytes,
        string mimeType,
        string systemInstruction,
        string userPrompt,
        string? apiKey = null,
        string? model = null,
        CancellationToken cancellationToken = default);
}

