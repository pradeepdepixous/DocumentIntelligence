namespace PixousDocIntelligence.Pipeline.Batching;

/// <summary>
/// Service responsible for Phase 3: Document mapping and batch scheduling.
/// Breaks large PDFs into overlapping page intervals and slices batch payloads.
/// </summary>
public interface IBatchScheduler
{
    /// <summary>
    /// Generates a reproducible BatchManifest with overlapping page intervals and source-reference metadata.
    /// </summary>
    BatchManifest CreateManifest(
        string jobId, 
        string documentId, 
        int totalPages, 
        int batchSize = 15, 
        int overlap = 3,
        string? sourceChecksum = null);

    /// <summary>
    /// Slices and extracts a specific sub-range of pages from the PDF into standalone PDF bytes.
    /// </summary>
    Task<byte[]> ExtractBatchBytesAsync(
        string originalPdfPath, 
        BatchDescriptor batch, 
        CancellationToken cancellationToken = default);
}
