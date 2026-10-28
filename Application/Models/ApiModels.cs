using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace PixousDocIntelligence.Application.Models;

/// <summary>
/// Dynamic LLM configuration options for a workflow or tool execution.
/// Allows per-call override of provider ('Gemini' or 'OpenAI'), model name, and API key.
/// </summary>
public class LLMInvocationOptions
{
    /// <summary>
    /// LLM Provider to execute this task ('Gemini' or 'OpenAI'). Defaults to server configuration if null.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Specific model identifier (e.g. 'gpt-4o-mini', 'gpt-4o', 'gemini-flash-latest'). Defaults to provider default if null.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Optional workflow-specific API key. When provided in Swagger, overrides the server environment key for this call.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Engine Tier: 'fast' (Gemini Flash-Lite, default) or 'premium' (OpenAI GPT-4o).
    /// </summary>
    public string? Tier { get; set; }
}

/// <summary>
/// Form data for submitting a document for processing.
/// </summary>
public class ProcessDocumentUploadRequest
{
    /// <summary>
    /// The PDF or document file to analyze (up to 100+ pages).
    /// </summary>
    [Required]
    public IFormFile File { get; set; } = null!;

    /// <summary>
    /// Optional natural language extraction instructions (e.g., 'Extract invoice line items, dates, and vendor details').
    /// If omitted, a dynamic broad business extraction plan is synthesized.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Optional tenant identifier for multi-tenant isolation. Default: 'default-tenant'.
    /// </summary>
    public string? TenantId { get; set; } = "default-tenant";

    /// <summary>
    /// Engine Tier: 'fast' (Gemini Flash-Lite, default) or 'premium' (OpenAI GPT-4o).
    /// </summary>
    public string? Tier { get; set; } = "fast";

    /// <summary>
    /// LLM Provider override for extraction planning and execution ('Gemini' or 'OpenAI').
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Optional model override (e.g. 'gpt-4o', 'gemini-flash-lite-latest').
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Optional workflow-specific API key. Overrides the server environment key for this processing job.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Extraction mode: 'schema' (structured fields, evidence citations, conflict ledger) or 'markdown' (verbatim layout transcription).
    /// Defaults to 'schema'.
    /// </summary>
    public string? Mode { get; set; } = "schema";

    /// <summary>
    /// If true, returns 202 Accepted immediately and processes the document in the background.
    /// Can also be activated via 'Prefer: respond-async' HTTP header.
    /// </summary>
    public bool? Async { get; set; }

    /// <summary>
    /// Optional manual JSON schema (ExtractionPlan JSON, standard JSON Schema draft-07/OpenAPI, or key-value dictionary).
    /// When provided, bypasses the LLM instruction planner for zero-latency, deterministic schema enforcement.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Optional manual JSON schema uploaded as a .json file.
    /// </summary>
    public IFormFile? SchemaFile { get; set; }
}

/// <summary>
/// Response returned immediately with HTTP 202 Accepted when a document is queued for background processing.
/// </summary>
public class AsyncJobAcceptedResponse
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public string Message { get; set; } = "Document queued for background processing.";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string StatusUrl { get; set; } = string.Empty;
    public string ResultUrl { get; set; } = string.Empty;
    public string EnvelopeUrl { get; set; } = string.Empty;
    public string CsvUrl { get; set; } = string.Empty;
    public string MarkdownUrl { get; set; } = string.Empty;
}

/// <summary>
/// Request for the Document Analyst Agent Summarizer Tool.
/// </summary>
public class AgentSummarizeRequest : LLMInvocationOptions
{
    /// <summary>
    /// Optional specific focus angle (e.g. 'pricing', 'legal liabilities', 'parties', 'services').
    /// </summary>
    public string? FocusArea { get; set; }
}

/// <summary>
/// Request for the Document Analyst Agent Grounded Q&amp;A Tool.
/// </summary>
public class AgentQuestionRequest : LLMInvocationOptions
{
    /// <summary>
    /// The question to answer against the verified document content.
    /// </summary>
    [Required]
    public string Question { get; set; } = string.Empty;
}

/// <summary>
/// Response payload for agent summaries.
/// </summary>
public class AgentSummarizeResponse
{
    public string JobId { get; set; } = string.Empty;
    public string? FocusArea { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
