using System.Text.Json;
using PixousDocIntelligence.Domain.Conflicts;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Normalization;

namespace PixousDocIntelligence.Pipeline.Stitching;

/// <summary>
/// Phase 5 Implementation: merges per-batch outputs into one cohesive stitched document.
/// Supports both:
/// 1. Schema Mode: reconciles field observations across overlapping batches, deduplicates,
///    resolves aliases, and preserves conflicting values in the ReconciliationLedger.
/// 2. Markdown Mode: merges per-page transcripts, deduplicating overlapping pages.
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
            SchemaVersion = plan.SchemaVersion > 0 ? plan.SchemaVersion : 2,
            CreatedAt = DateTime.UtcNow
        };

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

        // Deduplicate overlapping pages (group by page number and keep the most complete transcript)
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
            : "Document processed with evidence-backed extraction.";

        if (stitchedPages.Count > 0)
        {
            finalEnvelope.FullMarkdown = string.Join("\n\n---\n\n", stitchedPages.Select(p => $"<!-- Page {p.PageNumber} -->\n{p.Markdown}"));
        }

        // --- STRUCTURED FIELDS RECONCILIATION (SCHEMA MODE) ---
        bool hasItems = envelopesList.Any(e => e.Items.Count > 0);
        if (hasItems)
        {
            var aliasMap = BuildAliasMap(plan);

            // Collect all candidate observations across batches: (Item, BatchId)
            var allObservations = new List<(ExtractedItem Item, string BatchId)>();
            foreach (var env in envelopesList)
            {
                foreach (var item in env.Items)
                {
                    NormalizeItemValue(item);
                    allObservations.Add((item, env.BatchId));
                }
            }

            // Group by canonical FieldKey
            var groupedByField = allObservations
                .GroupBy(o => ResolveCanonicalKey(o.Item.FieldKey, aliasMap))
                .ToList();

            foreach (var fieldGroup in groupedByField)
            {
                string canonicalFieldKey = fieldGroup.Key;
                var plannedField = plan.DesiredFields.FirstOrDefault(f =>
                    f.FieldKey.Equals(canonicalFieldKey, StringComparison.OrdinalIgnoreCase) ||
                    f.Aliases.Any(a => a.Equals(canonicalFieldKey, StringComparison.OrdinalIgnoreCase)));

                // Group observations by PageNumber
                var byPage = fieldGroup.GroupBy(o => o.Item.PrimaryPageNumber).ToList();

                foreach (var pageGroup in byPage)
                {
                    int pageNum = pageGroup.Key;
                    var pageObservations = pageGroup.ToList();

                    if (pageObservations.Count == 1)
                    {
                        var singleItem = pageObservations[0].Item;
                        singleItem.FieldKey = canonicalFieldKey;
                        if (plannedField != null) singleItem.FieldLabel = plannedField.FieldLabel;
                        singleItem.ValidationStatus = "valid";
                        finalEnvelope.Items.Add(singleItem);

                        ledger.Decisions.Add(new FieldReconciliationDecision
                        {
                            FieldKey = canonicalFieldKey,
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
                    bool allValuesEqual = AreAllObservationsEqual(pageObservations);

                    if (allValuesEqual)
                    {
                        // Consistent observations from overlap: merge into one record, select highest confidence candidate
                        var bestCandidate = pageObservations
                            .OrderByDescending(x => x.Item.Confidence.Value)
                            .ThenByDescending(x => x.Item.PrimarySourceText.Length)
                            .First();

                        bestCandidate.Item.FieldKey = canonicalFieldKey;
                        if (plannedField != null) bestCandidate.Item.FieldLabel = plannedField.FieldLabel;
                        bestCandidate.Item.ValidationStatus = "valid";
                        finalEnvelope.Items.Add(bestCandidate.Item);

                        ledger.Decisions.Add(new FieldReconciliationDecision
                        {
                            FieldKey = canonicalFieldKey,
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
                        var conflict = new ConflictRecord
                        {
                            ConflictId = $"CONF-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                            FieldKey = canonicalFieldKey,
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
                            FieldKey = canonicalFieldKey,
                            PageNumber = pageNum,
                            Outcome = ReconciliationOutcome.ConflictPreserved,
                            Rationale = conflict.ResolutionRationale,
                            SelectedValue = $"[CONFLICT - {conflict.Candidates.Count} Candidates]",
                            SelectedConfidence = pageObservations.Min(o => o.Item.Confidence.Value),
                            SourceBatchIds = pageObservations.Select(o => o.BatchId).Distinct().ToList(),
                            CompetingCandidates = conflict.Candidates
                        });

                        finalEnvelope.Items.Add(new ExtractedItem
                        {
                            ItemType = "field",
                            FieldKey = canonicalFieldKey,
                            FieldLabel = plannedField?.FieldLabel ?? canonicalFieldKey,
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
        }

        // Deduplicate tables by TableKey and PageNumber
        var seenTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in envelopesList)
        {
            foreach (var table in env.Tables)
            {
                string key = $"{table.TableKey}:{table.PageNumber}";
                if (seenTables.Add(key))
                {
                    finalEnvelope.Tables.Add(table);
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
            if (!SemanticValueComparer.AreEquivalent(firstValueStr, currentStr))
            {
                return false;
            }
        }
        return true;
    }
}
