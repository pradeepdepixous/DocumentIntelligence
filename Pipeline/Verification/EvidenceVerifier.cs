using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using PixousDocIntelligence.Domain.Contracts;

namespace PixousDocIntelligence.Pipeline.Verification;

/// <summary>
/// Phase 6 Implementation: Multi-layer quality assurance and deterministic evidence verification.
/// Verifies that LLM-reported quotes actually exist verbatim on source PDF pages and calibrates
/// confidence scores down if citations cannot be grounded.
/// </summary>
public class EvidenceVerifier : IEvidenceVerifier
{
    private static readonly Regex DateCandidatePattern = new(
        @"\b(?:[A-Za-z]{3,9}\.?\s+\d{1,2},?\s+\d{2,4}|\d{1,2}[\/\-\.]\d{1,2}[\/\-\.]\d{2,4}|\d{4}-\d{2}-\d{2})\b",
        RegexOptions.Compiled);

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

        report.TotalItemsChecked = envelope.Items.Count;

        // Track per-page counts
        var pageItemCounts = new Dictionary<int, int>();
        var pageTableCounts = new Dictionary<int, int>();

        // 2. Perform Evidence Verification if structured items exist (Schema Mode)
        if (envelope.Items.Count > 0)
        {
            foreach (var item in envelope.Items)
            {
                int primaryPage = item.PrimaryPageNumber;
                pageItemCounts[primaryPage] = pageItemCounts.GetValueOrDefault(primaryPage) + 1;

                if (item.ValueType == "conflict")
                {
                    continue; // Conflicts are handled via the conflict records loop below
                }

                bool anySourceOnScannedPage = false;
                int verifiedSourceCount = 0;
                var unverifiedSources = new List<SourceEvidence>();

                foreach (var source in item.Sources)
                {
                    if (source.PageNumber < 1 || source.PageNumber > report.TotalPages)
                    {
                        unverifiedSources.Add(source);
                        continue;
                    }

                    if (pageIsScannedMap.GetValueOrDefault(source.PageNumber))
                    {
                        anySourceOnScannedPage = true;
                    }

                    string pageContent = pageTextMap.GetValueOrDefault(source.PageNumber, string.Empty);
                    if (IsQuotePresentOnPage(source.SourceText, pageContent))
                    {
                        verifiedSourceCount++;
                    }
                    else
                    {
                        unverifiedSources.Add(source);
                    }
                }

                bool allVerified = item.Sources.Count > 0 && verifiedSourceCount == item.Sources.Count;

                // Check whether the extracted value is supported by the cited evidence quote
                var valueEntries = GetValueEntriesForVerification(item);
                string rawEvidence = string.Join(" ", item.Sources.Select(s => s.SourceText));
                string cleanedEvidence = CleanForMatching(rawEvidence);

                var unsupportedValues = new List<string>();
                foreach (var entry in valueEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;

                    bool supported = IsValueSupported(entry, rawEvidence, cleanedEvidence);
                    if (!supported)
                    {
                        unsupportedValues.Add(entry);
                    }
                }

                bool allValuesSupported = unsupportedValues.Count == 0;

                if (allVerified && allValuesSupported)
                {
                    report.VerifiedCount++;
                    item.ValidationStatus = "verified";
                    item.Confidence.Source = "evidence_grounded";
                }
                else if (allVerified && !allValuesSupported)
                {
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
                        ReviewerNotes = $"Value(s) not found within the cited evidence text: {string.Join("; ", unsupportedValues)}. The citation is real but may not fully support this value."
                    });
                }
                else if (anySourceOnScannedPage && verifiedSourceCount == 0)
                {
                    report.VisionVerifiedCount++;
                    item.ValidationStatus = "vision_verified_scanned_page";
                    item.Confidence.Source = "vlm_vision_ocr";
                }
                else
                {
                    report.UnverifiedEvidenceCount++;
                    item.ValidationStatus = verifiedSourceCount > 0 ? "partially_verified" : "unverified_evidence";
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
        }

        // 4. Calculate page coverage for both modes
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
                ExtractedItemCount = pageItemCounts.GetValueOrDefault(p),
                TableCount = pageTableCounts.GetValueOrDefault(p),
                HasMarkdownContent = pagesWithMarkdown.Contains(p)
            })
            .ToList();

        report.PagesWithNoExtractedContent = report.PageCoverage
            .Where(pc => pc.ExtractedItemCount == 0 && pc.TableCount == 0 && !pc.HasMarkdownContent)
            .Select(pc => pc.PageNumber)
            .ToList();

        return Task.FromResult(report);
    }

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

    private static bool IsQuotePresentOnPage(string? sourceText, string pageContent)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(pageContent))
            return false;

        string cleanedQuote = CleanForMatching(sourceText);
        if (string.IsNullOrEmpty(cleanedQuote)) return false;

        if (pageContent.Contains(cleanedQuote))
            return true;

        // If not directly contained, handle quotes containing ellipsis (... or …), line breaks, or delimiters
        var rawSegments = sourceText.Split(new[] { "...", "…", "\r\n", "\n", "\r", " | ", "---" }, StringSplitOptions.RemoveEmptyEntries);
        var validSegments = rawSegments
            .Select(CleanForMatching)
            .Where(s => s.Length >= 6)
            .ToList();

        if (validSegments.Count > 0)
        {
            int matchedSegments = validSegments.Count(seg => pageContent.Contains(seg));
            double matchRatio = (double)matchedSegments / validSegments.Count;
            if (matchRatio >= 0.80 || matchedSegments == validSegments.Count)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValueSupported(string entry, string rawEvidence, string cleanedEvidence)
    {
        if (string.IsNullOrWhiteSpace(entry)) return true;

        string cleanedEntry = CleanForMatching(entry);
        if (string.IsNullOrWhiteSpace(cleanedEntry)) return true;

        // 1. Direct substring match
        if (cleanedEvidence.Contains(cleanedEntry)) return true;

        // 2. Date match
        if (IsDateSupportedByEvidence(entry, rawEvidence)) return true;

        // 3. Number and token overlap for long sentences/clauses
        var entryWords = cleanedEntry.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .ToList();

        if (entryWords.Count >= 4)
        {
            var entryNumbers = Regex.Matches(entry, @"\b\d+(?:[\.,]\d+)*%?\b")
                .Select(m => m.Value.Replace(",", "").Replace(".", "").Trim())
                .Where(n => n.Length > 0)
                .ToList();

            string rawEvidenceDigitsOnly = new string(rawEvidence.Where(char.IsDigit).ToArray());
            bool numbersSupported = true;
            if (entryNumbers.Count > 0)
            {
                int matchedNumbers = entryNumbers.Count(n => rawEvidenceDigitsOnly.Contains(n));
                numbersSupported = ((double)matchedNumbers / entryNumbers.Count) >= 0.75;
            }

            int matchedWords = entryWords.Count(w => cleanedEvidence.Contains(w));
            double wordRatio = (double)matchedWords / entryWords.Count;

            if (wordRatio >= 0.70 && numbersSupported)
            {
                return true;
            }
        }
        else if (entryWords.Count > 0)
        {
            // For short values (like organization names), check if all significant words appear in evidence
            if (entryWords.All(w => cleanedEvidence.Contains(w)))
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

