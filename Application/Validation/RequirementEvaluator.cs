using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Domain.Validation;
using PixousDocIntelligence.Infrastructure.AI.Validation.Models;

namespace PixousDocIntelligence.Application.Validation;

public class RequirementEvaluator : IRequirementEvaluator
{
    public List<RequirementCheckResult> Evaluate(
        string expectedDocumentType,
        IReadOnlyList<ValidationRequirement> requirements,
        GeminiDocumentObservation observation,
        DecisionPolicy policy)
    {
        var results = new List<RequirementCheckResult>();
        double minConfidence = policy.MinConfidenceThreshold > 0 ? policy.MinConfidenceThreshold : 0.80;

        foreach (var req in requirements)
        {
            var match = observation.Observations
                .FirstOrDefault(o => string.Equals(o.RequirementId, req.Id, StringComparison.OrdinalIgnoreCase));

            // Case A: Model omitted or missed returning an observation for this requirement
            if (match == null)
            {
                results.Add(new RequirementCheckResult
                {
                    RequirementId = req.Id,
                    Status = "uncertain",
                    Evidence = "Observation not returned or addressed in document analysis.",
                    Confidence = 0.0,
                    Required = req.Required
                });
                continue;
            }

            string state = match.ObservationState?.Trim().ToLowerInvariant() ?? "ambiguous";
            string evidence = match.Evidence?.Trim() ?? string.Empty;
            double confidence = match.Confidence;

            // Handle DocumentClassification requirement type specifically
            if (req.Type == RequirementType.DocumentClassification)
            {
                bool typeMatches = !string.IsNullOrWhiteSpace(observation.DetectedDocumentType) &&
                    (observation.DetectedDocumentType.Contains(expectedDocumentType, StringComparison.OrdinalIgnoreCase) ||
                     expectedDocumentType.Contains(observation.DetectedDocumentType, StringComparison.OrdinalIgnoreCase));

                if (!typeMatches && state == "missing")
                {
                    results.Add(new RequirementCheckResult
                    {
                        RequirementId = req.Id,
                        Status = "failed",
                        Evidence = $"Identified as '{observation.DetectedDocumentType}', not '{expectedDocumentType}'.",
                        Confidence = observation.DocumentTypeConfidence > 0 ? observation.DocumentTypeConfidence : confidence,
                        Required = req.Required
                    });
                    continue;
                }
            }

            // Case B: Content reported as clearly present
            if (state == "present")
            {
                if (string.IsNullOrWhiteSpace(evidence))
                {
                    // Present claimed without evidence
                    results.Add(new RequirementCheckResult
                    {
                        RequirementId = req.Id,
                        Status = "uncertain",
                        Evidence = "Marked present but no supporting evidence quoted.",
                        Confidence = confidence,
                        Required = req.Required
                    });
                }
                else if (confidence < minConfidence)
                {
                    // Below calibrated confidence threshold
                    results.Add(new RequirementCheckResult
                    {
                        RequirementId = req.Id,
                        Status = "uncertain",
                        Evidence = $"Evidence found ('{evidence}') but confidence ({confidence:P0}) is below threshold ({minConfidence:P0}).",
                        Confidence = confidence,
                        Required = req.Required
                    });
                }
                else if (!string.IsNullOrWhiteSpace(req.ExpectedValue) && 
                         !evidence.Contains(req.ExpectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    // Pattern or expected value mismatch
                    results.Add(new RequirementCheckResult
                    {
                        RequirementId = req.Id,
                        Status = "failed",
                        Evidence = $"Evidence ('{evidence}') does not match expected value/pattern '{req.ExpectedValue}'.",
                        Confidence = confidence,
                        Required = req.Required
                    });
                }
                else
                {
                    // Successfully passed
                    results.Add(new RequirementCheckResult
                    {
                        RequirementId = req.Id,
                        Status = "passed",
                        Evidence = evidence,
                        Confidence = confidence,
                        Required = req.Required
                    });
                }
            }
            // Case C: Content reported as missing
            else if (state == "missing")
            {
                results.Add(new RequirementCheckResult
                {
                    RequirementId = req.Id,
                    Status = "failed",
                    Evidence = !string.IsNullOrWhiteSpace(evidence) ? evidence : "Not found in document.",
                    Confidence = confidence,
                    Required = req.Required
                });
            }
            // Case D: Content ambiguous or unreadable
            else
            {
                results.Add(new RequirementCheckResult
                {
                    RequirementId = req.Id,
                    Status = "uncertain",
                    Evidence = !string.IsNullOrWhiteSpace(evidence) ? evidence : "Content is ambiguous, unreadable, or partially obscured.",
                    Confidence = confidence,
                    Required = req.Required
                });
            }
        }

        return results;
    }
}

