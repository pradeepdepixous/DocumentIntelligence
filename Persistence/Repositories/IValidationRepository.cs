using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Persistence.Repositories;

public interface IValidationRepository
{
    Task<ValidationProfileEntity?> GetProfileAsync(string profileId, string tenantId, CancellationToken cancellationToken = default);
    Task<List<ValidationProfileEntity>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken = default);
    Task SaveProfileAsync(ValidationProfileEntity entity, CancellationToken cancellationToken = default);
    Task SaveValidationResultAsync(ValidationResultEntity result, CancellationToken cancellationToken = default);
    Task<ValidationResultEntity?> GetValidationResultAsync(string validationId, string tenantId, CancellationToken cancellationToken = default);
}

