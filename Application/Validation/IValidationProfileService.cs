using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Domain.Validation;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Application.Validation;

public interface IValidationProfileService
{
    Task<ValidationProfileDto> CreateProfileAsync(CreateValidationProfileRequest request, string tenantId, CancellationToken cancellationToken = default);
    Task<ValidationProfileDto?> GetProfileAsync(string profileId, string tenantId, CancellationToken cancellationToken = default);
    Task<List<ValidationProfileDto>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<ValidationProfileDto?> UpdateProfileAsync(string profileId, UpdateValidationProfileRequest request, string tenantId, CancellationToken cancellationToken = default);
    Task SaveValidationResultAsync(ValidationResponse response, string tenantId, string? profileId = null, int? profileVersion = null, CancellationToken cancellationToken = default);
    Task<ValidationResultEntity?> GetValidationResultAsync(string validationId, string tenantId, CancellationToken cancellationToken = default);
}

