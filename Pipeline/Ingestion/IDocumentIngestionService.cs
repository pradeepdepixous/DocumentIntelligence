namespace PixousDocIntelligence.Pipeline.Ingestion;

/// <summary>
/// Service responsible for Phase 1: Ingestion, validation, SHA-256 checksumming,
/// page-count detection, and job creation.
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>
    /// Ingests and validates a local PDF document, computing its checksum, detecting page count,
    /// and initializing a trackable DocumentJob.
    /// </summary>
    Task<DocumentJob> IngestDocumentAsync(
        string filePath, 
        string tenantId = "default-tenant", 
        CancellationToken cancellationToken = default);
}
