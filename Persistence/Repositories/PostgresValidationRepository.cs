using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Persistence.Repositories;

public class PostgresValidationRepository : IValidationRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgresValidationRepository> _logger;

    public PostgresValidationRepository(
        IServiceScopeFactory scopeFactory,
        ILogger<PostgresValidationRepository> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ValidationProfileEntity?> GetProfileAsync(string profileId, string tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        return await db.ValidationProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProfileId == profileId && (p.TenantId == tenantId || p.TenantId == "default-tenant"), cancellationToken);
    }

    public async Task<List<ValidationProfileEntity>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        return await db.ValidationProfiles
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId || p.TenantId == "default-tenant")
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveProfileAsync(ValidationProfileEntity entity, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        var existing = await db.ValidationProfiles
            .FirstOrDefaultAsync(p => p.ProfileId == entity.ProfileId && p.TenantId == entity.TenantId, cancellationToken);

        if (existing == null)
        {
            await db.ValidationProfiles.AddAsync(entity, cancellationToken);
        }
        else
        {
            existing.Name = entity.Name;
            existing.Description = entity.Description;
            existing.DocumentType = entity.DocumentType;
            existing.Version = entity.Version;
            existing.RequirementsJson = entity.RequirementsJson;
            existing.DecisionPolicyJson = entity.DecisionPolicyJson;
            existing.IsActive = entity.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveValidationResultAsync(ValidationResultEntity result, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        await db.ValidationResults.AddAsync(result, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ValidationResultEntity?> GetValidationResultAsync(string validationId, string tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();

        return await db.ValidationResults
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ValidationId == validationId && (r.TenantId == tenantId || r.TenantId == "default-tenant"), cancellationToken);
    }
}

