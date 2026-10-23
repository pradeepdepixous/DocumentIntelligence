using System.Security.Cryptography;
using UglyToad.PdfPig;

namespace PixousDocIntelligence.Pipeline.Ingestion;

/// <summary>
/// Phase 1 Implementation: Document ingestion, validation, and page counting.
/// </summary>
public class DocumentIngestionService : IDocumentIngestionService
{
    public async Task<DocumentJob> IngestDocumentAsync(
        string filePath, 
        string tenantId = "default-tenant", 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The specified document does not exist: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            throw new InvalidOperationException("The specified file is empty (0 bytes).");
        }

        // Validate extension
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension != ".pdf")
        {
            throw new NotSupportedException($"Only PDF documents are supported for batch ingestion. Provided: '{extension}'");
        }

        // 1. Calculate SHA-256 Checksum
        string checksum = await ComputeSha256ChecksumAsync(filePath, cancellationToken);

        // 2. Inspect PDF structure and count pages using PdfPig
        int pageCount;
        try
        {
            using var pdfDoc = PdfDocument.Open(filePath);
            pageCount = pdfDoc.NumberOfPages;

            if (pdfDoc.IsEncrypted)
            {
                throw new InvalidOperationException("Encrypted/Password-protected PDFs are not supported without credentials.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidDataException($"Failed to parse PDF document structure. File may be corrupted. Error: {ex.Message}", ex);
        }

        if (pageCount <= 0)
        {
            throw new InvalidDataException("The PDF document does not contain any readable pages.");
        }

        // 3. Initialize DocumentJob
        var job = new DocumentJob
        {
            JobId = $"JOB-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            DocumentId = $"DOC-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            TenantId = tenantId,
            FileName = Path.GetFileName(filePath),
            FilePath = Path.GetFullPath(filePath),
            FileSizeBytes = fileInfo.Length,
            Sha256Checksum = checksum,
            PageCount = pageCount,
            Status = JobStatus.Created,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return job;
    }

    private static async Task<string> ComputeSha256ChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        byte[] hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
