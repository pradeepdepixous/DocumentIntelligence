using UglyToad.PdfPig;
using PixousDocIntelligence.Domain.Contracts;

namespace PixousDocIntelligence.Pipeline.Verification;

/// <summary>
/// Phase 6 Implementation: analyzes the source PDF (per-page text, scanned-page detection) and
/// reports page coverage for the stitched envelope.
///
/// Note: the extraction prompt (GeminiBatchExtractor/OpenAiBatchExtractor) is markdown-first —
/// Items/Tables are always empty by design. There is deliberately no per-item citation
/// verification or confidence calibration here anymore; that logic only ever ran against those
/// always-empty collections, so keeping it would be dead code duplicating a model the extractor
/// no longer produces. Coverage is now judged by whether a page got a real markdown transcript
/// (see HasMarkdownContent on PageCoverage), not by item/table citations.
/// </summary>
public class EvidenceVerifier : IEvidenceVerifier
{
    public Task<VerificationReport> VerifyEvidenceAndCalibrateAsync(
        string pdfPath,
        DocumentEnvelope envelope,
        QualityPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        policy ??= new QualityPolicy();

        var report = new VerificationReport
        {
            CalibratedEnvelope = envelope
        };

        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException($"PDF file not found for evidence verification: {pdfPath}");
        }

        // Analyze and cache text per page from PDF using PdfPig
        var pageTextMap = new Dictionary<int, string>();
        var pageIsScannedMap = new Dictionary<int, bool>();

        using (var pdfDoc = PdfDocument.Open(pdfPath))
        {
            report.TotalPages = pdfDoc.NumberOfPages;
            for (int p = 1; p <= pdfDoc.NumberOfPages; p++)
            {
                var page = pdfDoc.GetPage(p);
                var words = page.GetWords().Select(w => w.Text).ToList();
                string combined = string.Join(" ", words) + " " + page.Text;
                string cleaned = CleanForMatching(combined);

                pageTextMap[p] = cleaned;
                // If a page has virtually no digital text characters, it is a scanned image or raster photograph
                bool isScanned = words.Count < 3 && cleaned.Length < 15;
                pageIsScannedMap[p] = isScanned;
                if (isScanned)
                {
                    report.ScannedPageCount++;
                }
            }
        }

        report.TotalItemsChecked = envelope.Items.Count;

        // A page with a real markdown transcript has content even though Items/Tables are expected
        // to stay empty under the markdown-first extraction design — without this, every page would
        // wrongly show as "no content found" despite transcription having actually succeeded.
        const int minMeaningfulMarkdownLength = 20;
        var pagesWithMarkdown = envelope.Pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Markdown) && p.Markdown.Trim().Length >= minMeaningfulMarkdownLength)
            .Select(p => p.PageNumber)
            .ToHashSet();

        report.PageCoverage = pageTextMap.Keys
            .OrderBy(p => p)
            .Select(p => new PageCoverage
            {
                PageNumber = p,
                IsScanned = pageIsScannedMap.GetValueOrDefault(p),
                ExtractedItemCount = 0,
                TableCount = 0,
                HasMarkdownContent = pagesWithMarkdown.Contains(p)
            })
            .ToList();

        report.PagesWithNoExtractedContent = report.PageCoverage
            .Where(pc => !pc.HasMarkdownContent)
            .Select(pc => pc.PageNumber)
            .ToList();

        return Task.FromResult(report);
    }

    private static string CleanForMatching(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).ToLowerInvariant().Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
