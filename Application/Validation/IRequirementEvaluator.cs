using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Domain.Validation;
using PixousDocIntelligence.Infrastructure.AI.Validation.Models;

namespace PixousDocIntelligence.Application.Validation;

public interface IRequirementEvaluator
{
    List<RequirementCheckResult> Evaluate(
        string expectedDocumentType,
        IReadOnlyList<ValidationRequirement> requirements,
        GeminiDocumentObservation observation,
        DecisionPolicy policy);
}

