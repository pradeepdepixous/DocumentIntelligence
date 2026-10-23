using PixousDocIntelligence.Application.Models;

namespace PixousDocIntelligence.Application.Orchestration;

/// <summary>
/// Orchestration contract defining the end-to-end 7-phase document processing lifecycle.
/// </summary>
public interface IDocumentProcessingOrchestrator
{
    Task<DocumentProcessingResult> ProcessDocumentAsync(
        DocumentProcessingRequest request, 
        IProgress<string>? progress = null, 
        CancellationToken cancellationToken = default);
}
