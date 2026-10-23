using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Orchestration;

namespace PixousDocIntelligence.Application.UseCases;

/// <summary>
/// Primary use case for processing a document through the document intelligence pipeline.
/// </summary>
public class ProcessDocumentUseCase
{
    private readonly IDocumentProcessingOrchestrator _orchestrator;

    public ProcessDocumentUseCase(IDocumentProcessingOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<DocumentProcessingResult> ExecuteAsync(
        DocumentProcessingRequest request, 
        IProgress<string>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        return _orchestrator.ProcessDocumentAsync(request, progress, cancellationToken);
    }
}
