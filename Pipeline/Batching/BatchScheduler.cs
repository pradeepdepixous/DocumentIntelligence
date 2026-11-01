using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace PixousDocIntelligence.Pipeline.Batching;

/// <summary>
/// Phase 3 implementation: Computes stable overlapping page ranges and handles PDF slicing.
/// Explicitly links source document checksums and page-range metadata.
/// </summary>
public class BatchScheduler : IBatchScheduler
{
    public BatchManifest CreateManifest(
        string jobId, 
        string documentId, 
        int totalPages, 
        int batchSize = 15, 
        int overlap = 3,
        string? sourceChecksum = null)
    {
        if (totalPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalPages), "Total pages must be greater than 0.");
        }

        if (batchSize <= 0) batchSize = 15;
        if (overlap < 0) overlap = 0;
        if (overlap >= batchSize) overlap = batchSize / 2;

        var manifest = new BatchManifest
        {
            JobId = jobId,
            DocumentId = documentId,
            SourceDocumentChecksum = sourceChecksum,
            TotalPages = totalPages,
            BatchSize = batchSize,
            OverlapSize = overlap
        };

        // If the document is smaller than or equal to batchSize, process in a single batch
        if (totalPages <= batchSize)
        {
            manifest.Batches.Add(new BatchDescriptor
            {
                BatchId = "BATCH-001",
                JobId = jobId,
                DocumentId = documentId,
                SourceDocumentChecksum = sourceChecksum,
                StartPage = 1,
                EndPage = totalPages,
                OverlapStartPage = null,
                OverlapEndPage = null,
                Status = BatchStatus.Pending
            });
            return manifest;
        }

        int step = batchSize - overlap;
        int currentStart = 1;
        int batchIndex = 1;

        while (currentStart <= totalPages)
        {
            int currentEnd = Math.Min(currentStart + batchSize - 1, totalPages);

            int? overlapStart = (currentStart > 1) ? currentStart : null;
            int? overlapEnd = (currentEnd < totalPages) ? (currentEnd - overlap + 1) : null;

            manifest.Batches.Add(new BatchDescriptor
            {
                BatchId = $"BATCH-{batchIndex:D3}",
                JobId = jobId,
                DocumentId = documentId,
                SourceDocumentChecksum = sourceChecksum,
                StartPage = currentStart,
                EndPage = currentEnd,
                OverlapStartPage = overlapStart,
                OverlapEndPage = overlapEnd,
                Status = BatchStatus.Pending
            });

            if (currentEnd >= totalPages)
            {
                break;
            }

            currentStart += step;
            batchIndex++;
        }

        return manifest;
    }

    public IPdfBatchSource OpenBatchSource(string originalPdfPath)
    {
        if (!File.Exists(originalPdfPath))
        {
            throw new FileNotFoundException($"PDF file not found: {originalPdfPath}");
        }

        return new PdfBatchSource(originalPdfPath);
    }

    /// <summary>
    /// Opens the source PDF once (via PdfPig) and reuses that parsed document to slice out
    /// every batch's page range, instead of re-opening/re-parsing the whole file per batch.
    /// </summary>
    private sealed class PdfBatchSource : IPdfBatchSource
    {
        private readonly string _originalPdfPath;
        private readonly PdfDocument _sourceDoc;

        public PdfBatchSource(string originalPdfPath)
        {
            _originalPdfPath = originalPdfPath;
            _sourceDoc = PdfDocument.Open(originalPdfPath);
        }

        public Task<byte[]> ExtractBatchBytesAsync(BatchDescriptor batch, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // If the batch covers the entire document, return the original file directly
            if (batch.StartPage == 1 && batch.EndPage == _sourceDoc.NumberOfPages)
            {
                return File.ReadAllBytesAsync(_originalPdfPath, cancellationToken);
            }

            var builder = new PdfDocumentBuilder();
            for (int pageNum = batch.StartPage; pageNum <= batch.EndPage; pageNum++)
            {
                builder.AddPage(_sourceDoc, pageNum);
            }

            return Task.FromResult(builder.Build());
        }

        public void Dispose() => _sourceDoc.Dispose();
    }
}
