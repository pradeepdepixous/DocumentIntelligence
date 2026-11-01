using PixousDocIntelligence.Application.Contracts.Validation;

namespace PixousDocIntelligence.Application.Validation;

public interface IValidationOrchestrator
{
    Task<ValidationResponse> ValidateAsync(
        ValidateDocumentUploadRequest upload,
        string tenantId = "default-tenant",
        string? apiKey = null,
        string? model = null,
        CancellationToken cancellationToken = default);
}
