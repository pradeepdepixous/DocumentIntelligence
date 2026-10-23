using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence.Application.Models;

public class DocumentProcessingRequest
{
    public string DocumentPath { get; set; } = string.Empty;
    public string? UserInstructions { get; set; }
    public string TenantId { get; set; } = "default-tenant";
    public string? ResumeJobId { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? Tier { get; set; }
}

public class DocumentProcessingResult
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Status { get; set; } = "Succeeded";
    public ExtractionPlan Plan { get; set; } = new();
    public BatchManifest Manifest { get; set; } = new();
    public DocumentEnvelope StitchedEnvelope { get; set; } = new();
    public ReconciliationLedger ReconciliationLedger { get; set; } = new();
    public VerificationReport VerificationReport { get; set; } = new();

    public string OutputDirectory { get; set; } = string.Empty;
    public string EnvelopeJsonPath { get; set; } = string.Empty;
    public string ExtractedCsvPath { get; set; } = string.Empty;
    public string? ReviewQueueCsvPath { get; set; }
    public string ReconciliationLedgerPath { get; set; } = string.Empty;
    public long ExecutionDurationMs { get; set; }
}
