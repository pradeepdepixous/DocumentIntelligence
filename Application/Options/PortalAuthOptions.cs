namespace PixousDocIntelligence.Application.Options;

/// <summary>
/// Configuration for the customer portal's Google Sign-In + JWT session (distinct from the
/// ApiKeyScheme used by document-processing endpoints).
/// </summary>
public class PortalAuthOptions
{
    /// <summary>OAuth 2.0 client ID from Google Cloud Console; also the expected JWT audience to validate against Google.</summary>
    public string GoogleClientId { get; set; } = string.Empty;

    /// <summary>Symmetric key used to sign/verify the portal's own session JWTs. Must be at least 32 bytes.</summary>
    public string JwtSigningKey { get; set; } = string.Empty;

    public string JwtIssuer { get; set; } = "PixousDocIntelligence";

    public string JwtAudience { get; set; } = "PixousDocIntelligencePortal";

    public int JwtExpiryMinutes { get; set; } = 10080; // 7 days

    /// <summary>Email addresses that receive the "admin" role in their portal session on sign-in.</summary>
    public List<string> AdminEmails { get; set; } = new();

    /// <summary>
    /// Page-processing credit granted to every new account on signup. Shared across the account's
    /// own self-serve API keys AND Playground testing via the portal session — one pool, spent
    /// either way, resetting monthly like the legacy per-key quota it replaces for self-serve keys.
    /// </summary>
    public int DefaultAccountCreditLimit { get; set; } = 1000;
}
