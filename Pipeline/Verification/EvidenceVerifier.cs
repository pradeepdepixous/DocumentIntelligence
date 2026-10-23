using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using PixousDocIntelligence.Domain.Contracts;

namespace PixousDocIntelligence.Pipeline.Verification;

/// <summary>
/// Phase 6 Implementation: Validates model-reported citations against PDF page text,
/// intelligently detects scanned/photograph/raster pages without text layers,
/// calibrates confidence scores, and routes anomalies to the human review queue.
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

        // 1. Analyze and cache text per page from PDF using PdfPig
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

        // Tracks how many items/tables cite each page, to detect coverage gaps independent of
        // the raw item count (a document can have "enough" fields yet leave whole pages untouched).
        var pageItemCounts = new Dictionary<int, int>();
        var pageTableCounts = new Dictionary<int, int>();

        report.TotalItemsChecked = envelope.Items.Count;

        // 2. Deterministic verification of each item's evidence, source by source.
        // A field whose value aggregates facts from several pages carries one SourceEvidence
        // per contributing page, so each citation is checked and aligned independently instead
        // of trusting only the first one.
        foreach (var item in envelope.Items)
        {
            if (item.Sources.Count == 0)
            {
                item.Sources.Add(new SourceEvidence());
            }

            int verifiedSourceCount = 0;
            bool anySourceOnScannedPage = false;
            bool anySourceFuzzyMatch = false;
            var unverifiedSources = new List<SourceEvidence>();

            foreach (var source in item.Sources)
            {
                string sourceQuote = CleanForMatching(source.SourceText);
                bool isVerified = false;
                bool isExactMatch = false;
                bool targetPageIsScanned = pageIsScannedMap.TryGetValue(source.PageNumber, out bool scanned) && scanned;

                // Check the reported page, and adjacent page if close to boundary
                var candidatePages = new[] { source.PageNumber, source.PageNumber - 1, source.PageNumber + 1 }
                    .Where(p => pageTextMap.ContainsKey(p));

                foreach (var p in candidatePages)
                {
                    string pageContent = pageTextMap[p];
                    if (string.IsNullOrWhiteSpace(sourceQuote)) continue;

                    if (pageContent.Contains(sourceQuote, StringComparison.OrdinalIgnoreCase))
                    {
                        isVerified = true;
                        isExactMatch = true;
                        source.PageNumber = p; // Align to verified page
                        break;
                    }

                    // Check keyword token overlap
                    var tokens = sourceQuote.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length >= 3).ToList();

                    if (tokens.Count > 0)
                    {
                        int matches = tokens.Count(t => pageContent.Contains(t, StringComparison.OrdinalIgnoreCase));
                        if (((double)matches / tokens.Count) >= 0.60)
                        {
                            isVerified = true;
                            source.PageNumber = p;
                            break;
                        }
                    }
                }

                if (isVerified)
                {
                    verifiedSourceCount++;
                    if (!isExactMatch)
                    {
                        anySourceFuzzyMatch = true;
                    }
                }
                else
                {
                    unverifiedSources.Add(source);
                    if (targetPageIsScanned)
                    {
                        anySourceOnScannedPage = true;
                    }
                }
            }

            bool allVerified = unverifiedSources.Count == 0;

            foreach (var source in item.Sources)
            {
                pageItemCounts[source.PageNumber] = pageItemCounts.GetValueOrDefault(source.PageNumber) + 1;
            }

            // A cited quote existing on the claimed page only proves the CITATION is real; it says
            // nothing about whether the asserted VALUE is actually what that citation supports (a
            // hallucinated or mis-parsed value can sit right next to a perfectly real quote). Check
            // each individual value entry against the combined evidence text for this item.
            string rawCombinedEvidence = string.Join(" ", item.Sources.Select(s => s.SourceText));
            string combinedEvidence = CleanForMatching(rawCombinedEvidence);
            var unsupportedValues = new List<string>();
            bool anyValueFuzzyMatch = false;
            foreach (var valueText in GetValueEntriesForVerification(item))
            {
                string cleanedValue = CleanForMatching(valueText);
                if (string.IsNullOrWhiteSpace(cleanedValue)) continue;
                if (combinedEvidence.Contains(cleanedValue, StringComparison.OrdinalIgnoreCase)) continue;

                var valueTokens = cleanedValue.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 3).ToList();
                bool tokenSupported = valueTokens.Count > 0 &&
                    ((double)valueTokens.Count(t => combinedEvidence.Contains(t, StringComparison.OrdinalIgnoreCase)) / valueTokens.Count) >= 0.60;

                if (tokenSupported)
                {
                    anyValueFuzzyMatch = true;
                    continue;
                }

                // Literal matching fails for a value the model already reformatted (e.g. a
                // normalized "2026-05-15" sitting next to its human-readable quote "May 15, 2026").
                // Fall back to date-aware comparison before declaring it unsupported.
                if (IsDateSupportedByEvidence(valueText, rawCombinedEvidence))
                {
                    anyValueFuzzyMatch = true;
                    continue;
                }

                unsupportedValues.Add(valueText);
            }
            bool allValuesSupported = unsupportedValues.Count == 0;

            if (allVerified && allValuesSupported)
            {
                report.VerifiedCount++;
                item.ValidationStatus = "evidence_confirmed";

                // A model-reported confidence of 1.0 is never proof of correctness — it's just an
                // unverified self-assessment. "Evidence confirmed" only means the citation is real
                // and the value appears in it, so cap the score according to match quality instead
                // of trusting whatever the model claimed; an exact match earns a higher ceiling than
                // a fuzzy token-overlap match.
                double ceiling = (anySourceFuzzyMatch || anyValueFuzzyMatch) ? 0.85 : 0.97;
                double modelReported = item.Confidence.Value;
                item.Confidence.Value = Math.Min(modelReported, ceiling);
                if (item.Confidence.Value < modelReported)
                {
                    item.Confidence.CalibrationAdjustment = item.Confidence.Value - modelReported;
                }
                item.Confidence.Source = "rule_verified";
            }
            else if (allVerified && !allValuesSupported)
            {
                // Citations are real and on the claimed page(s), but at least one asserted value
                // cannot be found within the cited evidence text itself — do not call this confirmed.
                report.UnverifiedEvidenceCount++;
                item.ValidationStatus = "value_unsupported";
                double penalty = policy.UnverifiedPenalty;
                item.Confidence.CalibrationAdjustment = -penalty;
                item.Confidence.Value = Math.Max(0.10, item.Confidence.Value - penalty);
                item.Confidence.Source = "rule_verified";

                report.ReviewTasks.Add(new ReviewTask
                {
                    JobId = envelope.JobId,
                    FieldKey = item.FieldKey,
                    PageNumber = item.PrimaryPageNumber,
                    Reason = "value_unsupported",
                    ReportedValue = item.Value,
                    ReportedConfidence = item.Confidence.Value,
                    SourceQuote = item.PrimarySourceText,
                    ReviewerNotes = $"Value(s) not found within the cited evidence text: {string.Join("; ", unsupportedValues)}. The citation is real but may not actually support this value."
                });
            }
            else if (anySourceOnScannedPage && verifiedSourceCount == 0)
            {
                // Scanned or image-only pages: Gemini extracted this using its multimodal vision encoder.
                // Do not penalize confidence as an unverified hallucination.
                report.VisionVerifiedCount++;
                item.ValidationStatus = "vision_verified_scanned_page";
                item.Confidence.Source = "vlm_vision_ocr";
            }
            else
            {
                report.UnverifiedEvidenceCount++;
                item.ValidationStatus = verifiedSourceCount > 0 ? "partially_verified" : "unverified_evidence";
                // Calibrate confidence downward when evidence cannot be verified on a digital text page
                double penalty = policy.UnverifiedPenalty;
                item.Confidence.CalibrationAdjustment = -penalty;
                item.Confidence.Value = Math.Max(0.10, item.Confidence.Value - penalty);

                foreach (var source in unverifiedSources)
                {
                    report.ReviewTasks.Add(new ReviewTask
                    {
                        JobId = envelope.JobId,
                        FieldKey = item.FieldKey,
                        PageNumber = source.PageNumber,
                        Reason = "unverified_evidence",
                        ReportedValue = item.Value,
                        ReportedConfidence = item.Confidence.Value,
                        SourceQuote = source.SourceText,
                        ReviewerNotes = $"Source quote was not found on digital text Page {source.PageNumber}. Potential hallucination or wrong page attribution."
                    });
                }
            }

            // Check if calibrated confidence falls below review threshold
            if (item.Confidence.Value < policy.ReviewThreshold && (allVerified || anySourceOnScannedPage) && allValuesSupported)
            {
                report.LowConfidenceCount++;
                item.ValidationStatus = "flagged_low_confidence";

                report.ReviewTasks.Add(new ReviewTask
                {
                    JobId = envelope.JobId,
                    FieldKey = item.FieldKey,
                    PageNumber = item.PrimaryPageNumber,
                    Reason = "low_confidence",
                    ReportedValue = item.Value,
                    ReportedConfidence = item.Confidence.Value,
                    SourceQuote = item.PrimarySourceText,
                    ReviewerNotes = $"Reported confidence ({item.Confidence.Value:P0}) is below review threshold ({policy.ReviewThreshold:P0})."
                });
            }
        }

        foreach (var table in envelope.Tables)
        {
            pageTableCounts[table.PageNumber] = pageTableCounts.GetValueOrDefault(table.PageNumber) + 1;
        }

        report.PageCoverage = pageTextMap.Keys
            .OrderBy(p => p)
            .Select(p => new PageCoverage
            {
                PageNumber = p,
                IsScanned = pageIsScannedMap.GetValueOrDefault(p),
                ExtractedItemCount = pageItemCounts.GetValueOrDefault(p),
                TableCount = pageTableCounts.GetValueOrDefault(p)
            })
            .ToList();

        report.PagesWithNoExtractedContent = report.PageCoverage
            .Where(pc => pc.ExtractedItemCount == 0 && pc.TableCount == 0)
            .Select(pc => pc.PageNumber)
            .ToList();

        // 3. Automatically route any Unresolved Conflicts to the Review Queue
        foreach (var conflict in envelope.UnresolvedConflicts)
        {
            var firstCandidate = conflict.Candidates.FirstOrDefault();

            report.ReviewTasks.Add(new ReviewTask
            {
                JobId = envelope.JobId,
                FieldKey = conflict.FieldKey,
                PageNumber = firstCandidate?.PageNumber ?? 1,
                Reason = "unresolved_conflict",
                ReportedValue = conflict.Candidates.Select(c => c.Value).ToList(),
                ReportedConfidence = conflict.Candidates.Count > 0 ? conflict.Candidates.Average(c => c.Confidence) : 0.50,
                SourceQuote = string.Join(" | ", conflict.Candidates.Select(c => c.SourceText)),
                ReviewerNotes = conflict.ResolutionRationale ?? "Conflicting values extracted across overlapping batches."
            });
        }

        return Task.FromResult(report);
    }

    /// <summary>
    /// Splits an item's value into the individual entries that should each be checkable against
    /// the cited evidence text (one entry per array element, or a single entry for a scalar value).
    /// </summary>
    private static List<string> GetValueEntriesForVerification(ExtractedItem item)
    {
        var entries = new List<string>();

        if (item.Value is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in elem.EnumerateArray())
                {
                    entries.Add(e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.ToString());
                }
            }
            else if (elem.ValueKind != JsonValueKind.Null && elem.ValueKind != JsonValueKind.Undefined)
            {
                entries.Add(elem.ValueKind == JsonValueKind.String ? elem.GetString() ?? string.Empty : elem.ToString());
            }
        }
        else if (item.Value is System.Collections.IEnumerable list && item.Value is not string)
        {
            foreach (var entry in list)
            {
                if (entry != null) entries.Add(entry.ToString()!);
            }
        }
        else if (item.Value != null)
        {
            entries.Add(item.Value.ToString() ?? string.Empty);
        }

        return entries;
    }

    private static readonly Regex DateCandidatePattern = new(
        @"\b(?:[A-Za-z]{3,9}\.?\s+\d{1,2},?\s+\d{2,4}|\d{1,2}[\/\-\.]\d{1,2}[\/\-\.]\d{2,4}|\d{4}-\d{2}-\d{2})\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Recognizes a date value against its evidence even when the model already reformatted the
    /// value (e.g. normalized "2026-05-15") while the citation still quotes the original
    /// human-readable form (e.g. "May 15, 2026") — a literal substring/token match would miss this.
    /// </summary>
    private static bool IsDateSupportedByEvidence(string valueText, string rawEvidence)
    {
        if (!DateTime.TryParse(valueText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valueDate))
        {
            return false;
        }

        foreach (Match candidate in DateCandidatePattern.Matches(rawEvidence))
        {
            if (DateTime.TryParse(candidate.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var candidateDate) &&
                candidateDate.Date == valueDate.Date)
            {
                return true;
            }
        }

        return false;
    }

    private static string CleanForMatching(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).ToLowerInvariant().Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
