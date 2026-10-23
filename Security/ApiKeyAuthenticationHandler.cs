using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PixousDocIntelligence.Credentials;

namespace PixousDocIntelligence.Security;

/// <summary>
/// Authenticates requests using project access keys provided in 'X-Document-Api-Key' or 'Authorization: Bearer [key]'.
/// Validates against the central ICredentialRegistry, ensuring key hashes match active, non-expired credentials.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private readonly ICredentialRegistry _credentialRegistry;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ICredentialRegistry credentialRegistry)
        : base(options, logger, encoder)
    {
        _credentialRegistry = credentialRegistry;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? providedKey = null;

        // 1. Check dedicated X-Document-Api-Key header
        if (Request.Headers.TryGetValue(ApiKeyAuthenticationSchemeOptions.HeaderName, out var headerValues))
        {
            providedKey = headerValues.FirstOrDefault();
        }

        // 2. Fallback to standard Authorization: Bearer <key>
        if (string.IsNullOrWhiteSpace(providedKey) && Request.Headers.TryGetValue("Authorization", out var authValues))
        {
            string? authHeader = authValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    providedKey = authHeader["Bearer ".Length..].Trim();
                }
                else if (authHeader.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                {
                    providedKey = authHeader["ApiKey ".Length..].Trim();
                }
            }
        }

        // If no credentials were supplied on this request
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return AuthenticateResult.NoResult();
        }

        // 3. Cryptographically validate against the central hashed registry
        var client = await _credentialRegistry.ValidateKeyAsync(providedKey, Context.RequestAborted);
        if (client == null)
        {
            Logger.LogWarning("Authentication failed for access key with prefix '{Prefix}'", KeyHasher.ExtractPrefix(providedKey));
            return AuthenticateResult.Fail("Invalid, revoked, or expired Document Intelligence access key.");
        }

        // 4. Construct trusted ClaimsPrincipal
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, client.ClientId),
            new(ClaimTypes.Name, client.Name),
            new("client_id", client.ClientId),
            new("project_name", client.Name)
        };

        foreach (var perm in client.Permissions)
        {
            claims.Add(new Claim(ClaimTypes.Role, perm));
            claims.Add(new Claim("permission", perm));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // Store client details in HttpContext.Items for downstream controllers & logging
        Context.Items["ClientId"] = client.ClientId;
        Context.Items["ClientName"] = client.Name;

        return AuthenticateResult.Success(ticket);
    }
}
