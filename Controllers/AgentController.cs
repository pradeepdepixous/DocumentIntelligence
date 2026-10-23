using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PixousDocIntelligence.Agents;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Models;
using PixousDocIntelligence.Persistence.Repositories;
using PixousDocIntelligence.Services.LLM;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Endpoints for interacting with the Grounded Document Analyst Agent (Route B - Lightweight).
/// Answers and summaries are strictly bound to verified document extractions with page citations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly IDocumentProcessingRepository _repository;
    private readonly ILLMClientFactory _llmClientFactory;

    public AgentController(
        IDocumentProcessingRepository repository,
        ILLMClientFactory llmClientFactory)
    {
        _repository = repository;
        _llmClientFactory = llmClientFactory;
    }

    /// <summary>
    /// Executes the Document Analyst Agent Summarizer Tool to generate an executive or focus-specific summary.
    /// Dynamically routes to Gemini or OpenAI based on the request options.
    /// </summary>
    /// <param name="jobId">The unique Job ID whose extracted data should be analyzed.</param>
    /// <param name="request">Optional focus angle, provider ('Gemini' or 'OpenAI'), model, and API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("jobs/{jobId}/summarize")]
    [Authorize(Roles = "summarize,admin")]
    [ProducesResponseType(typeof(AgentSummarizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summarize(
        string jobId,
        [FromBody] AgentSummarizeRequest? request,
        CancellationToken cancellationToken = default)
    {
        var envelope = await _repository.GetStitchedEnvelopeAsync(jobId, cancellationToken);
        if (envelope == null)
        {
            return NotFound(new { error = $"Document envelope for Job '{jobId}' not found. Please ensure the document has completed processing." });
        }

        try
        {
            string? provider = Sanitize(request?.Provider) ?? Sanitize(Request.Headers["X-LLM-Provider"].FirstOrDefault());
            string? apiKey = Sanitize(request?.ApiKey) ?? Sanitize(Request.Headers["X-LLM-ApiKey"].FirstOrDefault());
            string? model = Sanitize(request?.Model) ?? Sanitize(Request.Headers["X-LLM-Model"].FirstOrDefault());
            string? tier = Sanitize(request?.Tier);
            string? focusArea = Sanitize(request?.FocusArea);

            var llmClient = _llmClientFactory.GetClient(provider, apiKey, model, tier);
            var agent = await CreateAgentForEnvelopeAsync(envelope, llmClient, cancellationToken);
            string summary = await agent.SummarizeAsync(focusArea, cancellationToken);

            return Ok(new AgentSummarizeResponse
            {
                JobId = jobId,
                FocusArea = focusArea,
                Summary = summary,
                GeneratedAt = DateTime.UtcNow
            });
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429"))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "LLM API rate limit or quota exceeded (HTTP 429).",
                details = httpEx.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "An error occurred while generating the summary.",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Executes the Document Analyst Agent Grounded Q&amp;A Tool. 
    /// Answers user queries with strict page citations, confidence metrics, and warnings for absent data.
    /// </summary>
    /// <param name="jobId">The unique Job ID whose extracted data should be queried.</param>
    /// <param name="request">The question to answer, plus optional provider ('Gemini' or 'OpenAI'), model, and API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("jobs/{jobId}/ask")]
    [Authorize(Roles = "ask,admin")]
    [ProducesResponseType(typeof(QnAResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AskQuestion(
        string jobId,
        [FromBody] AgentQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        string? question = Sanitize(request?.Question);
        if (string.IsNullOrWhiteSpace(question))
        {
            return BadRequest(new { error = "Question cannot be empty. Please provide a question to query the document." });
        }

        var envelope = await _repository.GetStitchedEnvelopeAsync(jobId, cancellationToken);
        if (envelope == null)
        {
            return NotFound(new { error = $"Document envelope for Job '{jobId}' not found. Please ensure the document has completed processing." });
        }

        try
        {
            string? provider = Sanitize(request?.Provider) ?? Sanitize(Request.Headers["X-LLM-Provider"].FirstOrDefault());
            string? apiKey = Sanitize(request?.ApiKey) ?? Sanitize(Request.Headers["X-LLM-ApiKey"].FirstOrDefault());
            string? model = Sanitize(request?.Model) ?? Sanitize(Request.Headers["X-LLM-Model"].FirstOrDefault());

            var llmClient = _llmClientFactory.GetClient(provider, apiKey, model);
            var agent = await CreateAgentForEnvelopeAsync(envelope, llmClient, cancellationToken);
            var result = await agent.AskQuestionAsync(question, cancellationToken);
            return Ok(result);
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429"))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "LLM API rate limit or quota exceeded (HTTP 429). Please wait before retrying or provide a paid key.",
                details = httpEx.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "An error occurred while answering the question.",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Executes Tool 3: Compliance &amp; Rule Validation Tool. 
    /// Audits a custom checklist of rules against the document with Pass/Fail knockout enforcement and weighted rubric scoring.
    /// </summary>
    /// <param name="jobId">The unique Job ID whose extracted data should be evaluated.</param>
    /// <param name="request">Checklist of rules, categories, max marks, knockout flags, and optional provider/apiKey.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("jobs/{jobId}/validate-rules")]
    [Authorize(Roles = "evaluate,admin")]
    [ProducesResponseType(typeof(RuleValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ValidateRules(
        string jobId,
        [FromBody] RuleValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.Rules == null || request.Rules.Count == 0)
        {
            return BadRequest(new { error = "Rule validation request must contain at least one rule." });
        }

        if (request.TotalMarksCap.HasValue && request.TotalMarksCap.Value > 0)
        {
            double sumMaxMarks = request.Rules.Sum(r => r.MaxMarks);
            if (sumMaxMarks > request.TotalMarksCap.Value)
            {
                return BadRequest(new
                {
                    error = $"The sum of rule max_marks ({sumMaxMarks}) exceeds total_marks_cap ({request.TotalMarksCap.Value}). Please adjust your rule marks to stay within the cap."
                });
            }
        }

        var envelope = await _repository.GetStitchedEnvelopeAsync(jobId, cancellationToken);
        if (envelope == null)
        {
            return NotFound(new { error = $"Document envelope for Job '{jobId}' not found. Please ensure the document has completed processing." });
        }

        try
        {
            string? provider = request.Provider ?? Request.Headers["X-LLM-Provider"].FirstOrDefault();
            string? apiKey = request.ApiKey ?? Request.Headers["X-LLM-ApiKey"].FirstOrDefault();
            string? model = request.Model ?? Request.Headers["X-LLM-Model"].FirstOrDefault();

            var llmClient = _llmClientFactory.GetClient(provider, apiKey, model);
            var agent = await CreateAgentForEnvelopeAsync(envelope, llmClient, cancellationToken);
            var result = await agent.ValidateRulesAsync(request, cancellationToken);
            result.JobId = jobId;
            result.DocumentId = envelope.DocumentId;
            return Ok(result);
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429"))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "LLM API rate limit or quota exceeded (HTTP 429). Please wait before retrying or use another provider/key.",
                details = httpEx.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "An error occurred while validating compliance rules.",
                details = ex.Message
            });
        }
    }

    private async Task<IDocAnalystAgent> CreateAgentForEnvelopeAsync(
        DocumentEnvelope envelope, 
        ILLMClient llmClient, 
        CancellationToken cancellationToken)
    {
        var agentDoc = new DocumentAnalysisResult
        {
            DocumentType = $"Schema_v{envelope.SchemaVersion}",
            OverallSummary = $"Stitched extraction for Job {envelope.JobId}",
            TotalPagesDetected = envelope.Items.SelectMany(i => i.Sources).Select(s => s.PageNumber).DefaultIfEmpty(1).Max(),
            OverallConfidence = envelope.Items.Count > 0 ? envelope.Items.Average(i => i.Confidence.Value) : 0.95
        };

        foreach (var item in envelope.Items)
        {
            agentDoc.Fields[item.FieldKey] = new FieldContent
            {
                Value = item.Value,
                Confidence = item.Confidence.Value,
                PageNumber = item.PrimaryPageNumber,
                EvidenceContext = item.PrimarySourceText
            };
        }

        string rawJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });

        // Extract full document text from the original PDF if available
        string? rawDocumentText = !string.IsNullOrWhiteSpace(envelope.FullMarkdown)
            ? envelope.FullMarkdown
            : null;

        if (string.IsNullOrWhiteSpace(rawDocumentText))
        {
            try
            {
                var doc = await _repository.GetDocumentAsync(envelope.DocumentId, cancellationToken);
                if (doc != null && !string.IsNullOrWhiteSpace(doc.FilePath) && System.IO.File.Exists(doc.FilePath))
                {
                    using var pdf = UglyToad.PdfPig.PdfDocument.Open(doc.FilePath);
                    var sb = new System.Text.StringBuilder();
                    foreach (var page in pdf.GetPages())
                    {
                        sb.AppendLine($"--- PAGE {page.Number} ---");
                        sb.AppendLine(page.Text);
                    }
                    rawDocumentText = sb.ToString();
                }
            }
            catch { /* Fallback to envelope only */ }
        }

        var agent = new DocAnalystAgent(llmClient);
        agent.LoadDocument(agentDoc, rawJson, rawDocumentText);
        return agent;
    }

    private static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string trimmed = input.Trim();
        return trimmed.Equals("string", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }
}
