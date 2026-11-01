using System.Text.Json;
using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Domain.Validation;
using PixousDocIntelligence.Infrastructure.AI.Validation;

namespace PixousDocIntelligence.Application.Validation;

public class ValidationOrchestrator : IValidationOrchestrator
{
    private readonly IDocumentAnalyzer _documentAnalyzer;
    private readonly ValidationPromptBuilder _promptBuilder;
    private readonly IRequirementEvaluator _evaluator;
    private readonly IValidationDecisionService _decisionService;
    private readonly IValidationProfileService _profileService;

    public ValidationOrchestrator(
        IDocumentAnalyzer documentAnalyzer,
        ValidationPromptBuilder promptBuilder,
        IRequirementEvaluator evaluator,
        IValidationDecisionService decisionService,
        IValidationProfileService profileService)
    {
        _documentAnalyzer = documentAnalyzer ?? throw new ArgumentNullException(nameof(documentAnalyzer));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
    }

    public async Task<ValidationResponse> ValidateAsync(
        ValidateDocumentUploadRequest upload,
        string tenantId = "default-tenant",
        string? apiKey = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        if (upload == null) throw new ArgumentNullException(nameof(upload));
        if (upload.File == null || upload.File.Length == 0)
        {
            throw new ArgumentException("A non-empty document file must be uploaded.", nameof(upload.File));
        }

        string expectedDocType;
        List<ValidationRequirement> requirements;
        DecisionPolicy policy;
        string? usedProfileId = null;
        int? usedProfileVersion = null;

        // 1. Resolve configuration from Profile (Approach B) or Inline (Approach A)
        if (!string.IsNullOrWhiteSpace(upload.ProfileId))
        {
            var profile = await _profileService.GetProfileAsync(upload.ProfileId.Trim(), tenantId, cancellationToken);
            if (profile == null)
            {
                throw new ArgumentException($"Validation profile '{upload.ProfileId}' was not found for tenant '{tenantId}'.");
            }
            if (!profile.IsActive)
            {
                throw new InvalidOperationException($"Validation profile '{upload.ProfileId}' is deactivated.");
            }

            expectedDocType = profile.DocumentType;
            requirements = profile.Requirements;
            policy = profile.DecisionPolicy;
            usedProfileId = profile.ProfileId;
            usedProfileVersion = profile.Version;
        }
        else if (!string.IsNullOrWhiteSpace(upload.RequirementsJson))
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var inlineConfig = JsonSerializer.Deserialize<InlineValidationConfiguration>(upload.RequirementsJson, jsonOptions);

                if (inlineConfig == null || inlineConfig.Requirements == null || inlineConfig.Requirements.Count == 0)
                {
                    throw new ArgumentException("Inline requirementsJson must contain at least one requirement.");
                }

                expectedDocType = !string.IsNullOrWhiteSpace(inlineConfig.ExpectedDocumentType)
                    ? inlineConfig.ExpectedDocumentType
                    : "Generic Document";
                requirements = inlineConfig.Requirements;
                policy = inlineConfig.DecisionPolicy ?? new DecisionPolicy();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Invalid RequirementsJson payload: {ex.Message}", ex);
            }
        }
        else
        {
            throw new ArgumentException("Validation requires either a valid 'profileId' or inline 'requirementsJson'.");
        }

        // 2. Read document bytes and determine MIME type
        byte[] fileBytes;
        using (var ms = new MemoryStream())
        {
            await upload.File.CopyToAsync(ms, cancellationToken);
            fileBytes = ms.ToArray();
        }

        string mimeType = !string.IsNullOrWhiteSpace(upload.File.ContentType)
            ? upload.File.ContentType
            : Path.GetExtension(upload.File.FileName).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "application/pdf"
            };

        // 3. Fast-fail page count guard (prevents token exhaustion on 100+ page documents)
        if (mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(fileBytes);
                if (pdfDoc.NumberOfPages > 10)
                {
                    throw new ArgumentException(
                        $"Validation is intended for certificates and single documents up to 10 pages. Uploaded document has {pdfDoc.NumberOfPages} pages.");
                }
            }
            catch (ArgumentException) { throw; }
            catch { /* Skip check if non-standard stream */ }
        }

        // 4. Build dynamic prompt for Gemini
        var (systemInstruction, userPrompt) = _promptBuilder.Build(expectedDocType, requirements);

        // 4. Send document to Gemini for objective evidence extraction
        var observations = await _documentAnalyzer.AnalyzeAsync(
            fileBytes,
            mimeType,
            systemInstruction,
            userPrompt,
            apiKey,
            model,
            cancellationToken);

        // 5. Evaluate observations using .NET deterministic rules
        var checks = _evaluator.Evaluate(expectedDocType, requirements, observations, policy);

        // 6. Determine final business decision
        var (status, isValid, message) = _decisionService.MakeDecision(checks, policy);

        var response = new ValidationResponse
        {
            ValidationId = $"val_{Guid.NewGuid():N}",
            DocumentType = !string.IsNullOrWhiteSpace(observations.DetectedDocumentType)
                ? observations.DetectedDocumentType
                : expectedDocType,
            Status = status,
            IsValid = isValid,
            Checks = checks,
            Message = message,
            ValidatedAtUtc = DateTime.UtcNow
        };

        // 7. Persist validation audit record to PostgreSQL
        try
        {
            await _profileService.SaveValidationResultAsync(
                response,
                tenantId,
                usedProfileId,
                usedProfileVersion,
                cancellationToken);
        }
        catch { /* Audit logging failure should not fail the validation result */ }

        return response;
    }
}
