using System.Text.Json;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Models;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Clients;

/// <summary>
/// Strongly-typed reusable client interface for consuming projects (Invoice Management, Government Portal, Legal Processing)
/// to interact with the centralized Pixous Document Intelligence service.
/// </summary>
public interface IDocumentIntelligenceClient
{
    /// <summary>
    /// Uploads and processes a multi-page document through the 7-phase extraction and verification pipeline.
    /// Supports 'fast' (Gemini Flash-Lite, default) and 'premium' (OpenAI GPT-4o) engine tiers.
    /// </summary>
    Task<DocumentProcessingResult> ProcessDocumentAsync(
        Stream documentStream, 
        string fileName, 
        string? instructions = null, 
        string? tier = "fast",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads and processes a local document file path.
    /// </summary>
    Task<DocumentProcessingResult> ProcessDocumentFileAsync(
        string filePath, 
        string? instructions = null, 
        string? tier = "fast",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes grounded Q&amp;A against a completed document extraction job.
    /// </summary>
    Task<QnAResult> AskQuestionAsync(
        string jobId, 
        string question, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesizes an executive or domain-focused summary from verified extractions.
    /// </summary>
    Task<AgentSummarizeResponse> SummarizeDocumentAsync(
        string jobId, 
        string? focusAngle = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates deterministic and heuristic business rules against extracted document data.
    /// </summary>
    Task<RuleValidationResponse> EvaluateRulesAsync(
        string jobId, 
        RuleValidationRequest request, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the job execution entity and status.
    /// </summary>
    Task<ExtractionJobEntity?> GetJobStatusAsync(
        string jobId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the RFC 4180-compliant CSV export for a completed job.
    /// </summary>
    Task<Stream> DownloadCsvAsync(
        string jobId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves live usage metrics and quota details for the authenticated client.
    /// </summary>
    Task<JsonDocument> GetMyUsageAsync(
        CancellationToken cancellationToken = default);
}
