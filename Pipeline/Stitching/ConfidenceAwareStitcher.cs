using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;

namespace PixousDocIntelligence.Pipeline.Stitching;

/// <summary>
/// Phase 5 Implementation: merges per-batch markdown transcripts into one stitched document.
/// Deduplicates overlapping pages (the same page can appear in two adjacent batches when
/// overlap_size &gt; 0), keeping the longer/more complete transcript for each page number, and
/// rebuilds the full document-level summary, keywords, and markdown.
///
/// Note: the extraction prompt (GeminiBatchExtractor/OpenAiBatchExtractor) is markdown-first —
/// Items/Tables are always empty by design, evidence lives in page markdown instead. There is
/// deliberately no field-level reconciliation here anymore (alias resolution, per-field conflict
/// detection, a populated ReconciliationLedger) — that logic only ever ran against those always-empty
/// collections, so keeping it would be dead code duplicating a model the extractor no longer produces.
/// </summary>
public class ConfidenceAwareStitcher : IStitchingEngine
{
    public DocumentEnvelope StitchBatches(
        IEnumerable<DocumentEnvelope> batchEnvelopes,
        ExtractionPlan plan)
    {
        return StitchBatchesDetailed(batchEnvelopes, plan).Envelope;
    }

    public StitchingResult StitchBatchesDetailed(
        IEnumerable<DocumentEnvelope> batchEnvelopes,
        ExtractionPlan plan)
    {
        var envelopesList = batchEnvelopes.ToList();
        if (envelopesList.Count == 0)
        {
            return new StitchingResult();
        }

        string docId = envelopesList.First().DocumentId;
        string jobId = envelopesList.First().JobId;

        var finalEnvelope = new DocumentEnvelope
        {
            DocumentId = docId,
            JobId = jobId,
            BatchId = "STITCHED-FINAL",
            SchemaVersion = 2,
            CreatedAt = DateTime.UtcNow
        };

        // The ledger stays in the response contract (DocumentProcessingResult.ReconciliationLedger)
        // for API shape stability, but with no decisions — there's nothing to reconcile when
        // batches never produce competing field-level observations in the first place.
        var ledger = new ReconciliationLedger
        {
            JobId = jobId,
            DocumentId = docId
        };

        var allPages = new List<PageMarkdownContent>();
        var allKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batchSummaries = new List<string>();

        foreach (var env in envelopesList)
        {
            finalEnvelope.Batches.AddRange(env.Batches);
            allPages.AddRange(env.Pages);

            foreach (var kw in env.DocumentKeywords)
            {
                if (!string.IsNullOrWhiteSpace(kw)) allKeywords.Add(kw.Trim());
            }

            if (!string.IsNullOrWhiteSpace(env.DocumentSummary))
            {
                batchSummaries.Add(env.DocumentSummary.Trim());
            }
        }

        // Deduplicate overlapping pages (e.g. Page 4 in Batch 1 and Batch 2): group by page
        // number and keep the longer/more complete transcript for each.
        var stitchedPages = allPages
            .GroupBy(p => p.PageNumber)
            .Select(g => g.OrderByDescending(p => p.Markdown.Length).First())
            .OrderBy(p => p.PageNumber)
            .ToList();

        finalEnvelope.Pages = stitchedPages;
        finalEnvelope.TotalPages = stitchedPages.Count > 0 ? stitchedPages.Max(p => p.PageNumber) : 0;
        finalEnvelope.DocumentKeywords = allKeywords.OrderBy(k => k).ToList();
        finalEnvelope.DocumentSummary = batchSummaries.Count > 0
            ? string.Join(" ", batchSummaries)
            : "Document processed with page-level Markdown transcription.";

        finalEnvelope.FullMarkdown = string.Join("\n\n---\n\n", stitchedPages.Select(p => $"<!-- Page {p.PageNumber} -->\n{p.Markdown}"));

        return new StitchingResult
        {
            Envelope = finalEnvelope,
            Ledger = ledger
        };
    }
}
