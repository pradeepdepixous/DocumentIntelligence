using System.Text;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Services.LLM;
using UglyToad.PdfPig;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Phase 4 OpenAI Batch Extractor.
/// Extracts text from batch PDF bytes using PdfPig, and sends structured prompts to OpenAI
/// using the configured IExtractionStrategy.
/// </summary>
public class OpenAiBatchExtractor : IBatchExtractor
{
    private readonly ILLMClient _llmClient;
    private readonly IExtractionStrategy? _strategy;

    public OpenAiBatchExtractor(ILLMClient llmClient, IExtractionStrategy? strategy = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _strategy = strategy;
    }

    public async Task<DocumentEnvelope> ExtractBatchAsync(
        byte[] batchPdfBytes, 
        BatchDescriptor batchDescriptor, 
        ExtractionPlan plan, 
        CancellationToken cancellationToken = default)
    {
        // 1. Extract text from the batch slice using PdfPig
        var sb = new StringBuilder();
        try
        {
            using var pdfDoc = PdfDocument.Open(batchPdfBytes);
            int pageIndex = batchDescriptor.StartPage;
            foreach (var page in pdfDoc.GetPages())
            {
                sb.AppendLine($"=== PAGE {pageIndex} ===");
                sb.AppendLine(page.Text);
                pageIndex++;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to extract text from batch slice {batchDescriptor.BatchId}: {ex.Message}", ex);
        }

        string batchText = sb.ToString();

        // 2. Prepare strategy-guided prompts
        var activeStrategy = _strategy ?? ExtractionStrategyResolver.Resolve(plan);
        var (systemInstruction, userPrompt) = activeStrategy.BuildPrompts(batchDescriptor, plan);

        string combinedPrompt = 
            $"DOCUMENT BATCH TEXT ({batchDescriptor.PageRangeLabel}):\n{batchText}\n\n" +
            userPrompt;

        // 3. Call OpenAI with JSON mode
        string responseJson = await _llmClient.GenerateContentAsync(systemInstruction, combinedPrompt, requireJson: true, cancellationToken);

        // 4. Parse into verified DocumentEnvelope via strategy
        return activeStrategy.ParseEnvelope(responseJson, batchDescriptor, plan);
    }
}
