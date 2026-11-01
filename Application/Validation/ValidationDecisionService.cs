using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Domain.Validation;

namespace PixousDocIntelligence.Application.Validation;

public class ValidationDecisionService : IValidationDecisionService
{
    public (string Status, bool? IsValid, string Message) MakeDecision(
        List<RequirementCheckResult> checks,
        DecisionPolicy policy)
    {
        var mandatoryChecks = checks.Where(c => c.Required).ToList();
        var optionalChecks = checks.Where(c => !c.Required).ToList();

        // 1. Mandatory requirement failures immediately disqualify the document
        var failedMandatory = mandatoryChecks.Where(c => c.Status == "failed").ToList();
        if (failedMandatory.Count > 0)
        {
            string failedIds = string.Join(", ", failedMandatory.Select(f => f.RequirementId));
            return (
                Status: "invalid",
                IsValid: false,
                Message: $"Document failed mandatory check(s): {failedIds}."
            );
        }

        // 2. Mandatory requirement uncertainties trigger the onUncertain policy
        var uncertainMandatory = mandatoryChecks.Where(c => c.Status == "uncertain").ToList();
        if (uncertainMandatory.Count > 0)
        {
            string uncertainIds = string.Join(", ", uncertainMandatory.Select(u => u.RequirementId));
            string policyAction = policy.OnUncertain?.Trim().ToLowerInvariant() ?? "manual_review";

            return policyAction switch
            {
                "invalid" => (
                    Status: "invalid",
                    IsValid: false,
                    Message: $"Mandatory check(s) uncertain and rejected by policy: {uncertainIds}."
                ),
                "valid" => (
                    Status: "valid",
                    IsValid: true,
                    Message: $"Mandatory check(s) uncertain but accepted by relaxed policy: {uncertainIds}."
                ),
                _ => (
                    Status: "manual_review",
                    IsValid: null,
                    Message: $"Document requires manual review due to uncertain mandatory check(s): {uncertainIds}."
                )
            };
        }

        // 3. All mandatory passed. Check for optional failures or warnings.
        var failedOptional = optionalChecks.Where(c => c.Status == "failed").ToList();
        var uncertainOptional = optionalChecks.Where(c => c.Status == "uncertain").ToList();

        if (failedOptional.Count > 0 || uncertainOptional.Count > 0)
        {
            int nonPassedCount = failedOptional.Count + uncertainOptional.Count;
            return (
                Status: "valid",
                IsValid: true,
                Message: $"Document is valid. All mandatory requirements satisfied ({nonPassedCount} non-critical advisory warning(s))."
            );
        }

        return (
            Status: "valid",
            IsValid: true,
            Message: "Document successfully satisfied all validation requirements."
        );
    }
}

