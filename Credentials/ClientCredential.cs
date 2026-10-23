using System.Text.Json.Serialization;

namespace PixousDocIntelligence.Credentials;

/// <summary>
/// Represents a registered client project with dedicated Document Intelligence access permissions.
/// The raw access key is never stored in plaintext—only its SHA-256 hash is kept.
/// </summary>
public class ClientCredential
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Portal account (Google-authenticated customer) that self-serve created this key, if any.
    /// Null for keys provisioned directly by an admin (e.g. the seeded default credentials).
    /// </summary>
    [JsonPropertyName("owner_account_id")]
    public string? OwnerAccountId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("key_hash")]
    public string KeyHash { get; set; } = string.Empty;

    [JsonPropertyName("key_prefix")]
    public string KeyPrefix { get; set; } = string.Empty;

    [JsonPropertyName("permissions")]
    public List<string> Permissions { get; set; } = new();

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("last_used_at")]
    public DateTime? LastUsedAt { get; set; }

    [JsonPropertyName("plan_tier")]
    public string PlanTier { get; set; } = "Standard";

    [JsonPropertyName("monthly_page_quota")]
    public int MonthlyPageQuota { get; set; } = 5000;

    [JsonPropertyName("pages_used_this_month")]
    public int PagesUsedThisMonth { get; set; } = 0;

    [JsonPropertyName("total_requests")]
    public long TotalRequests { get; set; } = 0;

    [JsonPropertyName("billing_cycle")]
    public string BillingCycle { get; set; } = DateTime.UtcNow.ToString("yyyy-MM");
}
