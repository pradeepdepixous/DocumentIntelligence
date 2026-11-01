namespace PixousDocIntelligence.Pipeline.Batching;

/// <summary>
/// A source PDF opened once and reused to slice out each batch's page range, so a
/// multi-batch document is parsed by PdfPig a single time instead of once per batch.
/// </summary>
public interface IPdfBatchSource : IDisposable
{
    /// <summary>
    /// Slices the given batch's page range into standalone PDF bytes.
    /// </summary>
    Task<byte[]> ExtractBatchBytesAsync(BatchDescriptor batch, CancellationToken cancellationToken = default);
}
