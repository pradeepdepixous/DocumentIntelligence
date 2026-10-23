using System.Text.Json;
using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Normalization;

namespace PixousDocIntelligence.Pipeline.Stitching;

/// <summary>
/// Phase 5 Implementation: Confidence-aware reconciliation and stitching engine.
/// Adheres strictly to the 5 stitching rules:
/// 1. Never deduplicate by value alone.
/// 2. Never deduplicate by page alone.
/// 3. Do not average confidence scores.
/// 4. Do not treat overlapping calls as independent votes.
/// 5. Preserve competing candidates in a conflict ledger.
/// Generates an explainable ReconciliationLedger detailing every reconciliation decision.
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

        var ledger = new ReconciliationLedger
        {
            JobId = jobId,
            DocumentId = docId
        };

        // --- STITCH MARKDOWN TRANSCRIPTS & RECONSTRUCT PAGES ---
        var allPages = new List<PageMarkdownContent>();
        var allKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batchSummaries = new List<string>();

        foreach (var env in envelopesList)
        {
            // Collect batch-level results
            finalEnvelope.Batches.AddRange(env.Batches);

            // Collect pages
            allPages.AddRange(env.Pages);

            // Collect keywords
            foreach (var kw in env.DocumentKeywords)
            {
                if (!string.IsNullOrWhiteSpace(kw)) allKeywords.Add(kw.Trim());
            }

            if (!string.IsNullOrWhiteSpace(env.DocumentSummary))
            {
                batchSummaries.Add(env.DocumentSummary.Trim());
            }
        }

        // Deduplicate overlapping pages (e.g. Page 4 in Batch 1 and Batch 2)
        // Group by PageNumber and pick the best representation (highest text length)
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

        // Assemble FullMarkdown transcript
        finalEnvelope.FullMarkdown = string.Join("\n\n---\n\n", stitchedPages.Select(p => $"<!-- Page {p.PageNumber} -->\n{p.Markdown}"));

        // 1. Build canonical alias mapping from the ExtractionPlan
        var aliasMap = BuildAliasMap(plan);

        // 2. Flatten and normalize all items across all batches
        var normalizedItems = new List<(ExtractedItem Item, string BatchId)>();
        foreach (var envelope in envelopesList)
        {
            foreach (var item in envelope.Items)
            {
                // Normalize field_key using alias mapping
                string canonicalKey = ResolveCanonicalKey(item.FieldKey, aliasMap);
                item.IsDynamicallyDiscovered = !aliasMap.ContainsKey(item.FieldKey) && !aliasMap.ContainsKey(canonicalKey);
                item.FieldKey = canonicalKey;

                // Normalize string values (trimming excess whitespace)
                NormalizeItemValue(item);

                normalizedItems.Add((item, envelope.BatchId));
            }

            // Collect tables
            foreach (var table in envelope.Tables)
            {
                finalEnvelope.Tables.Add(table);
            }
        }

        // 3. Reconcile items grouped by canonical FieldKey
        var groupedByKey = normalizedItems.GroupBy(x => x.Item.FieldKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedByKey)
        {
            string fieldKey = group.Key;
            var observations = group.ToList();

            // Find planned field definition
            var plannedField = plan.DesiredFields.FirstOrDefault(f => f.FieldKey.Equals(fieldKey, StringComparison.OrdinalIgnoreCase));

            // Group observations by source PageNumber
            var byPage = observations.GroupBy(x => x.Item.PrimaryPageNumber);

            foreach (var pageGroup in byPage)
            {
                int pageNum = pageGroup.Key;
                var pageObservations = pageGroup.ToList();

                if (pageObservations.Count == 1)
                {
                    // Single observation on this page, keep directly
                    var singleItem = pageObservations[0].Item;
                    finalEnvelope.Items.Add(singleItem);

                    ledger.Decisions.Add(new FieldReconciliationDecision
                    {
                        FieldKey = fieldKey,
                        PageNumber = pageNum,
                        Outcome = ReconciliationOutcome.SingleObservation,
                        Rationale = "Single observation identified from batch. No overlap collision on this page.",
                        SelectedValue = singleItem.Value,
                        SelectedConfidence = singleItem.Confidence.Value,
                        SelectedEvidence = singleItem.PrimarySourceText,
                        SourceBatchIds = new List<string> { pageObservations[0].BatchId }
                    });

                    continue;
                }

                // Multiple observations on the SAME page (from overlapping batches)
                // Check if they are consistent or conflicting
                bool allValuesEqual = AreAllObservationsEqual(pageObservations);

                if (allValuesEqual)
                {
                    // Consistent observations from overlap: merge into one record, select highest confidence candidate
                    var bestCandidate = pageObservations
                        .OrderByDescending(x => x.Item.Confidence.Value)
                        .ThenByDescending(x => x.Item.PrimarySourceText.Length)
                        .First();

                    bestCandidate.Item.ValidationStatus = "valid";
                    finalEnvelope.Items.Add(bestCandidate.Item);

                    ledger.Decisions.Add(new FieldReconciliationDecision
                    {
                        FieldKey = fieldKey,
                        PageNumber = pageNum,
                        Outcome = ReconciliationOutcome.OverlapMerged,
                        Rationale = $"Identical observation repeated across {pageObservations.Count} overlapping batches. Retained highest-confidence candidate without artificial score inflation.",
                        SelectedValue = bestCandidate.Item.Value,
                        SelectedConfidence = bestCandidate.Item.Confidence.Value,
                        SelectedEvidence = bestCandidate.Item.PrimarySourceText,
                        SourceBatchIds = pageObservations.Select(o => o.BatchId).Distinct().ToList(),
                        CompetingCandidates = pageObservations.Select(o => new CandidateObservation
                        {
                            BatchId = o.BatchId,
                            PageNumber = pageNum,
                            Value = o.Item.Value,
                            Confidence = o.Item.Confidence.Value,
                            SourceText = o.Item.PrimarySourceText
                        }).ToList()
                    });
                }
                else
                {
                    // CONFLICT DETECTED: Different values reported for the same field on the same page
                    // RULE 5: Preserve both candidates in the Conflict Ledger. Do not resolve via confidence alone!
                    var conflict = new ConflictRecord
                    {
                        ConflictId = $"CONF-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                        FieldKey = fieldKey,
                        Status = "unresolved",
                        ResolutionRationale = $"Contradiction detected across overlapping batches on Page {pageNum}. " +
                                              "Candidates disagree on exact value and require human verification."
                    };

                    foreach (var obs in pageObservations)
                    {
                        conflict.Candidates.Add(new CandidateObservation
                        {
                            BatchId = obs.BatchId,
                            PageNumber = pageNum,
                            Value = obs.Item.Value,
                            Confidence = obs.Item.Confidence.Value,
                            SourceText = obs.Item.PrimarySourceText
                        });
                    }

                    finalEnvelope.UnresolvedConflicts.Add(conflict);

                    ledger.Decisions.Add(new FieldReconciliationDecision
                    {
                        FieldKey = fieldKey,
                        PageNumber = pageNum,
                        Outcome = ReconciliationOutcome.ConflictPreserved,
                        Rationale = conflict.ResolutionRationale,
                        SelectedValue = $"[CONFLICT - {conflict.Candidates.Count} Candidates]",
                        SelectedConfidence = pageObservations.Min(o => o.Item.Confidence.Value),
                        SourceBatchIds = pageObservations.Select(o => o.BatchId).Distinct().ToList(),
                        CompetingCandidates = conflict.Candidates
                    });

                    // Add flagged placeholder in items so caller knows a conflict exists
                    finalEnvelope.Items.Add(new ExtractedItem
                    {
                        ItemType = "field",
                        FieldKey = fieldKey,
                        FieldLabel = plannedField?.FieldLabel ?? fieldKey,
                        Value = $"[CONFLICT - {conflict.Candidates.Count} Candidates]",
                        ValueType = "conflict",
                        Sources = new List<SourceEvidence>
                        {
                            new SourceEvidence
                            {
                                PageNumber = pageNum,
                                SourceText = string.Join(" | ", pageObservations.Select(o => o.Item.PrimarySourceText))
                            }
                        },
                        Confidence = new ConfidenceSignal
                        {
                            Value = pageObservations.Min(o => o.Item.Confidence.Value),
                            Source = "unresolved_conflict"
                        },
                        ValidationStatus = "flagged"
                    });
                }
            }
        }

        return new StitchingResult
        {
            Envelope = finalEnvelope,
            Ledger = ledger
        };
    }

    private static Dictionary<string, string> BuildAliasMap(ExtractionPlan plan)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in plan.DesiredFields)
        {
            map[field.FieldKey] = field.FieldKey;
            foreach (var alias in field.Aliases)
            {
                map[alias] = field.FieldKey;
            }
        }
        return map;
    }

    private static string ResolveCanonicalKey(string rawKey, Dictionary<string, string> aliasMap)
    {
        if (aliasMap.TryGetValue(rawKey, out string? canonical))
        {
            return canonical;
        }
        return rawKey.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
    }

    private static void NormalizeItemValue(ExtractedItem item)
    {
        if (item.Value is JsonElement elem && elem.ValueKind == JsonValueKind.String)
        {
            string str = elem.GetString()?.Trim() ?? string.Empty;
            item.Value = str;
        }
    }

    private static bool AreAllObservationsEqual(List<(ExtractedItem Item, string BatchId)> observations)
    {
        string firstValueStr = observations[0].Item.GetDisplayValue().Trim();
        for (int i = 1; i < observations.Count; i++)
        {
            string currentStr = observations[i].Item.GetDisplayValue().Trim();
            // Semantic comparison, not literal string equality: the same fact can be written
            // differently across overlapping batches (e.g. "May 15, 2026" vs "05/15/2026"), and
            // exact-match comparison alone would wrongly treat that as a conflict.
            if (!SemanticValueComparer.AreEquivalent(firstValueStr, currentStr))
            {
                return false;
            }
        }
        return true;
    }
}
