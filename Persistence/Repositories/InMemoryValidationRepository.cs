using System.Collections.Concurrent;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Persistence.Repositories;

public class InMemoryValidationRepository : IValidationRepository
{
    private readonly ConcurrentDictionary<string, ValidationProfileEntity> _profiles = new();
    private readonly ConcurrentDictionary<string, ValidationResultEntity> _results = new();

    public Task<ValidationProfileEntity?> GetProfileAsync(string profileId, string tenantId, CancellationToken cancellationToken = default)
    {
        string key = $"{tenantId}:{profileId}";
        if (_profiles.TryGetValue(key, out var profile))
        {
            return Task.FromResult<ValidationProfileEntity?>(profile);
        }

        return Task.FromResult<ValidationProfileEntity?>(null);
    }

    public Task<List<ValidationProfileEntity>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var list = _profiles.Values
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.UpdatedAt)
            .ToList();

        return Task.FromResult(list);
    }

    public Task SaveProfileAsync(ValidationProfileEntity entity, CancellationToken cancellationToken = default)
    {
        string key = $"{entity.TenantId}:{entity.ProfileId}";
        _profiles[key] = entity;
        return Task.CompletedTask;
    }

    public Task SaveValidationResultAsync(ValidationResultEntity result, CancellationToken cancellationToken = default)
    {
        string key = $"{result.TenantId}:{result.ValidationId}";
        _results[key] = result;
        return Task.CompletedTask;
    }

    public Task<ValidationResultEntity?> GetValidationResultAsync(string validationId, string tenantId, CancellationToken cancellationToken = default)
    {
        string key = $"{tenantId}:{validationId}";
        if (_results.TryGetValue(key, out var res))
        {
            return Task.FromResult<ValidationResultEntity?>(res);
        }

        return Task.FromResult<ValidationResultEntity?>(null);
    }
}

