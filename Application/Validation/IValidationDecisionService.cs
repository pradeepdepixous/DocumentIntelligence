using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Domain.Validation;

namespace PixousDocIntelligence.Application.Validation;

public interface IValidationDecisionService
{
    (string Status, bool? IsValid, string Message) MakeDecision(
        List<RequirementCheckResult> checks,
        DecisionPolicy policy);
}

