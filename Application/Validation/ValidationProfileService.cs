using System.Text.Json;
using System.Text.RegularExpressions;
using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Domain.Validation;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Persistence.Repositories;

namespace PixousDocIntelligence.Application.Validation;

public class ValidationProfileService : IValidationProfileService
{
    private readonly IValidationRepository _repository;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public ValidationProfileService(IValidationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ValidationProfileDto> CreateProfileAsync(CreateValidationProfileRequest request, string tenantId, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Profile name is required.");
        if (string.IsNullOrWhiteSpace(request.DocumentType)) throw new ArgumentException("Profile documentType is required.");
        if (request.Requirements == null || request.Requirements.Count == 0) throw new ArgumentException("Profile must contain at least one requirement.");

        string profileId = !string.IsNullOrWhiteSpace(request.ProfileId)
            ? request.ProfileId.Trim()
            : GenerateProfileId(request.Name);

        var policy = request.DecisionPolicy ?? new DecisionPolicy();

        var entity = new ValidationProfileEntity
        {
            ProfileId = profileId,
            TenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : "default-tenant",
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            DocumentType = request.DocumentType.Trim(),
            Version = 1,
            RequirementsJson = JsonSerializer.Serialize(request.Requirements, JsonOptions),
            DecisionPolicyJson = JsonSerializer.Serialize(policy, JsonOptions),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.SaveProfileAsync(entity, cancellationToken);

        return MapToDto(entity);
    }

    public async Task<ValidationProfileDto?> GetProfileAsync(string profileId, string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return null;

        var entity = await _repository.GetProfileAsync(profileId.Trim(), tenantId, cancellationToken);
        return entity != null ? MapToDto(entity) : null;
    }

    public async Task<List<ValidationProfileDto>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var entities = await _repository.ListProfilesAsync(tenantId, cancellationToken);
        return entities.Select(MapToDto).ToList();
    }

    public async Task<ValidationProfileDto?> UpdateProfileAsync(string profileId, UpdateValidationProfileRequest request, string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return null;

        var existing = await _repository.GetProfileAsync(profileId.Trim(), tenantId, cancellationToken);
        if (existing == null) return null;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            existing.Name = request.Name.Trim();
        }
        if (request.Description != null)
        {
            existing.Description = request.Description.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            existing.DocumentType = request.DocumentType.Trim();
        }
        if (request.Requirements != null && request.Requirements.Count > 0)
        {
            existing.RequirementsJson = JsonSerializer.Serialize(request.Requirements, JsonOptions);
        }
        if (request.DecisionPolicy != null)
        {
            existing.DecisionPolicyJson = JsonSerializer.Serialize(request.DecisionPolicy, JsonOptions);
        }
        if (request.IsActive.HasValue)
        {
            existing.IsActive = request.IsActive.Value;
        }

        // Increment version on update
        existing.Version += 1;
        existing.UpdatedAt = DateTime.UtcNow;

        await _repository.SaveProfileAsync(existing, cancellationToken);

        return MapToDto(existing);
    }

    public async Task SaveValidationResultAsync(
        ValidationResponse response, 
        string tenantId, 
        string? profileId = null, 
        int? profileVersion = null, 
        CancellationToken cancellationToken = default)
    {
        var entity = new ValidationResultEntity
        {
            ValidationId = response.ValidationId,
            TenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : "default-tenant",
            ProfileId = profileId,
            ProfileVersion = profileVersion,
            DocumentType = response.DocumentType,
            Status = response.Status,
            IsValid = response.IsValid,
            ChecksJson = JsonSerializer.Serialize(response.Checks, JsonOptions),
            Message = response.Message,
            ValidatedAt = response.ValidatedAtUtc
        };

        await _repository.SaveValidationResultAsync(entity, cancellationToken);
    }

    public async Task<ValidationResultEntity?> GetValidationResultAsync(string validationId, string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(validationId)) return null;
        return await _repository.GetValidationResultAsync(validationId.Trim(), tenantId, cancellationToken);
    }

    private static ValidationProfileDto MapToDto(ValidationProfileEntity entity)
    {
        var requirements = new List<ValidationRequirement>();
        try
        {
            requirements = JsonSerializer.Deserialize<List<ValidationRequirement>>(entity.RequirementsJson, JsonOptions) ?? new();
        }
        catch { /* Fallback to empty list */ }

        var policy = new DecisionPolicy();
        try
        {
            policy = JsonSerializer.Deserialize<DecisionPolicy>(entity.DecisionPolicyJson, JsonOptions) ?? new();
        }
        catch { /* Fallback to default */ }

        return new ValidationProfileDto
        {
            ProfileId = entity.ProfileId,
            TenantId = entity.TenantId,
            Name = entity.Name,
            Description = entity.Description,
            DocumentType = entity.DocumentType,
            Version = entity.Version,
            Requirements = requirements,
            DecisionPolicy = policy,
            IsActive = entity.IsActive,
            CreatedAtUtc = entity.CreatedAt,
            UpdatedAtUtc = entity.UpdatedAt
        };
    }

    private static string GenerateProfileId(string name)
    {
        string slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        if (slug.Length > 20) slug = slug[..20].TrimEnd('_');
        return $"prf_{slug}_{Guid.NewGuid():N}"[..28];
    }
}

