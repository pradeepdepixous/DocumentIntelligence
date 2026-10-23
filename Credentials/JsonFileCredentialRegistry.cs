using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PixousDocIntelligence.Credentials;

/// <summary>
/// Thread-safe file-backed implementation of the central credential registry.
/// Persists hashed credentials to a JSON file while retaining zero plaintext secrets in storage.
/// </summary>
public class JsonFileCredentialRegistry : ICredentialRegistry
{
    private readonly string _filePath;
    private readonly ILogger<JsonFileCredentialRegistry>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, ClientCredential> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonFileCredentialRegistry(string storageDirectory, ILogger<JsonFileCredentialRegistry>? logger = null)
    {
        _logger = logger;
        Directory.CreateDirectory(storageDirectory);
        _filePath = Path.Combine(storageDirectory, "credentials_registry.json");

        InitializeRegistry();
    }

    private void InitializeRegistry()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                string json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<ClientCredential>>(json, JsonOptions);
                if (list != null && list.Count > 0)
                {
                    foreach (var c in list)
                    {
                        _cache[c.KeyHash] = c;
                    }
                    _logger?.LogInformation("Loaded {Count} registered client credentials from {Path}", _cache.Count, _filePath);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load existing credentials registry from {Path}. Reseeding defaults.", _filePath);
            }
        }

        // Seed default multi-project credentials
        SeedDefaultCredentials();
    }

    private void SeedDefaultCredentials()
    {
        var seeded = new List<(string Id, string Name, string Key, List<string> Permissions)>
        {
            (
                "client_admin", 
                "Document Intelligence Service Administrator", 
                "pixous_admin_sec_live_994a821e90b7c4d123456789", 
                new List<string> { "process", "summarize", "ask", "evaluate", "admin" }
            ),
            (
                "client_inv_mgmt", 
                "Invoice Management System", 
                "pixous_inv_sec_live_38a0bc412f91d8e476210984", 
                new List<string> { "process", "summarize", "ask", "evaluate" }
            ),
            (
                "client_gov_portal", 
                "Government Portal", 
                "pixous_gov_sec_live_571cf3981da4f6b218904321", 
                new List<string> { "process", "summarize", "ask", "evaluate" }
            ),
            (
                "client_legal_proc", 
                "Legal Processing Service", 
                "pixous_leg_sec_live_a189f7243cb8d9e156473829", 
                new List<string> { "process", "summarize", "ask", "evaluate" }
            )
        };

        var records = new List<ClientCredential>();
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        PIXOUS DOCUMENT INTELLIGENCE — INITIALIZED CLIENT CREDENTIALS        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");

        foreach (var item in seeded)
        {
            string hash = KeyHasher.ComputeSha256Hash(item.Key);
            var cred = new ClientCredential
            {
                ClientId = item.Id,
                Name = item.Name,
                KeyHash = hash,
                KeyPrefix = KeyHasher.ExtractPrefix(item.Key),
                Permissions = item.Permissions,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            records.Add(cred);
            _cache[hash] = cred;

            Console.WriteLine($"• Project:     {item.Name} ({item.Id})");
            Console.WriteLine($"  Access Key:  {item.Key}");
            Console.WriteLine($"  Permissions: {string.Join(", ", item.Permissions)}\n");
        }
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────\n");

        SaveToFile();
    }

    private void SaveToFile()
    {
        try
        {
            string json = JsonSerializer.Serialize(_cache.Values.ToList(), JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist credentials registry to {Path}", _filePath);
        }
    }

    public async Task<ClientCredential?> ValidateKeyAsync(string rawKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;

        string hash = KeyHasher.ComputeSha256Hash(rawKey);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(hash, out var credential))
            {
                if (!credential.IsActive) return null;
                if (credential.ExpiresAt.HasValue && credential.ExpiresAt.Value < DateTime.UtcNow) return null;

                credential.LastUsedAt = DateTime.UtcNow;
                SaveToFile();
                return credential;
            }
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(ClientCredential Credential, string PlaintextKey)> CreateKeyAsync(
        string name,
        List<string> permissions,
        DateTime? expiresAt = null,
        string? ownerAccountId = null,
        CancellationToken cancellationToken = default)
    {
        string rawKey = KeyHasher.GenerateAccessKey();
        string hash = KeyHasher.ComputeSha256Hash(rawKey);
        string clientId = $"client_{Guid.NewGuid().ToString("N")[..8]}";

        var credential = new ClientCredential
        {
            ClientId = clientId,
            OwnerAccountId = ownerAccountId,
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed Client Project" : name.Trim(),
            KeyHash = hash,
            KeyPrefix = KeyHasher.ExtractPrefix(rawKey),
            Permissions = permissions.Count > 0 ? permissions : new List<string> { "process", "summarize", "ask" },
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        await _lock.WaitAsync(cancellationToken);
        try
        {
            _cache[hash] = credential;
            SaveToFile();
            return (credential, rawKey);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RevokeKeyAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var target = _cache.Values.FirstOrDefault(c => c.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            if (target == null) return false;

            target.IsActive = false;
            SaveToFile();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(ClientCredential Credential, string PlaintextKey)?> RotateKeyAsync(
        string clientId, 
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var oldEntry = _cache.Values.FirstOrDefault(c => c.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            if (oldEntry == null) return null;

            _cache.Remove(oldEntry.KeyHash);

            string newRawKey = KeyHasher.GenerateAccessKey();
            string newHash = KeyHasher.ComputeSha256Hash(newRawKey);

            oldEntry.KeyHash = newHash;
            oldEntry.KeyPrefix = KeyHasher.ExtractPrefix(newRawKey);
            oldEntry.IsActive = true;
            oldEntry.CreatedAt = DateTime.UtcNow;

            _cache[newHash] = oldEntry;
            SaveToFile();

            return (oldEntry, newRawKey);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ClientCredential?> GetClientByIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var c = _cache.Values.FirstOrDefault(x => x.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            return c != null ? CloneMasked(c) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(bool Allowed, int RemainingPages, string Message)> CheckQuotaAsync(
        string clientId, 
        int incomingPages, 
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var target = _cache.Values.FirstOrDefault(c => c.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return (false, 0, $"Client '{clientId}' not found in registry.");
            }

            // Auto-reset monthly counter if billing cycle changed
            string currentCycle = DateTime.UtcNow.ToString("yyyy-MM");
            if (!string.Equals(target.BillingCycle, currentCycle, StringComparison.OrdinalIgnoreCase))
            {
                target.BillingCycle = currentCycle;
                target.PagesUsedThisMonth = 0;
            }

            int remaining = Math.Max(0, target.MonthlyPageQuota - target.PagesUsedThisMonth);

            if (target.PagesUsedThisMonth + incomingPages > target.MonthlyPageQuota)
            {
                return (false, remaining, 
                    $"Monthly page quota exceeded for project '{target.Name}'. Quota: {target.MonthlyPageQuota:N0}, Used: {target.PagesUsedThisMonth:N0}, Requested: {incomingPages:N0}. Please upgrade your allocation.");
            }

            return (true, remaining, "Quota available.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordUsageAsync(
        string clientId, 
        int pagesProcessed, 
        string operation, 
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var target = _cache.Values.FirstOrDefault(c => c.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                string currentCycle = DateTime.UtcNow.ToString("yyyy-MM");
                if (!string.Equals(target.BillingCycle, currentCycle, StringComparison.OrdinalIgnoreCase))
                {
                    target.BillingCycle = currentCycle;
                    target.PagesUsedThisMonth = 0;
                }

                target.PagesUsedThisMonth += pagesProcessed;
                target.TotalRequests += 1;
                target.LastUsedAt = DateTime.UtcNow;
                SaveToFile();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ClientCredential>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _cache.Values.Select(CloneMasked).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ClientCredential>> ListKeysByOwnerAsync(string ownerAccountId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _cache.Values
                .Where(c => string.Equals(c.OwnerAccountId, ownerAccountId, StringComparison.Ordinal))
                .Select(CloneMasked)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static ClientCredential CloneMasked(ClientCredential c) => new()
    {
        ClientId = c.ClientId,
        OwnerAccountId = c.OwnerAccountId,
        Name = c.Name,
        KeyHash = "SHA256_MASKED",
        KeyPrefix = c.KeyPrefix,
        Permissions = new List<string>(c.Permissions),
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        ExpiresAt = c.ExpiresAt,
        LastUsedAt = c.LastUsedAt,
        PlanTier = c.PlanTier,
        MonthlyPageQuota = c.MonthlyPageQuota,
        PagesUsedThisMonth = c.PagesUsedThisMonth,
        TotalRequests = c.TotalRequests,
        BillingCycle = c.BillingCycle
    };
}
