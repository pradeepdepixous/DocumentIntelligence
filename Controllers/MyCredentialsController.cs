using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Credentials;
using PixousDocIntelligence.Security;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Self-serve API key management for a signed-in portal customer, scoped strictly to keys they
/// created themselves. Distinct from CredentialsController, which is the admin-only, cross-customer
/// equivalent. Callers here authenticate with their portal session JWT, not an API key.
/// </summary>
[ApiController]
[Route("api/my/credentials")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Bearer)]
public class MyCredentialsController : ControllerBase
{
    // Self-serve keys may never be granted "admin" — only the allowlisted portal admin identity gets that.
    private static readonly HashSet<string> AllowedSelfServePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "process", "summarize", "ask", "evaluate"
    };

    private readonly ICredentialRegistry _credentialRegistry;

    public MyCredentialsController(ICredentialRegistry credentialRegistry)
    {
        _credentialRegistry = credentialRegistry;
    }

    private string? CurrentAccountId => User.FindFirst("account_id")?.Value;

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ClientCredential>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyCredentials(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CurrentAccountId))
        {
            return Unauthorized(new { error = "Unable to determine portal account from session." });
        }

        var list = await _credentialRegistry.ListKeysByOwnerAsync(CurrentAccountId, cancellationToken);
        return Ok(list);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateMyCredential([FromBody] CreateCredentialRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CurrentAccountId))
        {
            return Unauthorized(new { error = "Unable to determine portal account from session." });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Key name is required." });
        }

        var requestedPermissions = (request.Permissions ?? new List<string> { "process" })
            .Where(p => AllowedSelfServePermissions.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requestedPermissions.Count == 0)
        {
            requestedPermissions.Add("process");
        }

        var (credential, plaintextKey) = await _credentialRegistry.CreateKeyAsync(
            request.Name,
            requestedPermissions,
            request.ExpiresAt,
            ownerAccountId: CurrentAccountId,
            cancellationToken: cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "API key created. Save this secret now; it cannot be recovered.",
            client_id = credential.ClientId,
            project_name = credential.Name,
            access_key = plaintextKey,
            key_prefix = credential.KeyPrefix,
            permissions = credential.Permissions,
            created_at = credential.CreatedAt,
            expires_at = credential.ExpiresAt
        });
    }

    [HttpPost("{clientId}/rotate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateMyCredential(string clientId, CancellationToken cancellationToken)
    {
        if (!await OwnsCredentialAsync(clientId, cancellationToken))
        {
            return NotFound(new { error = $"Key '{clientId}' not found." });
        }

        var result = await _credentialRegistry.RotateKeyAsync(clientId, cancellationToken);
        if (result == null)
        {
            return NotFound(new { error = $"Key '{clientId}' not found." });
        }

        return Ok(new
        {
            message = "Key rotated. Update your application with the new key immediately.",
            client_id = result.Value.Credential.ClientId,
            project_name = result.Value.Credential.Name,
            new_access_key = result.Value.PlaintextKey,
            key_prefix = result.Value.Credential.KeyPrefix,
            rotated_at = DateTime.UtcNow
        });
    }

    [HttpDelete("{clientId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeMyCredential(string clientId, CancellationToken cancellationToken)
    {
        if (!await OwnsCredentialAsync(clientId, cancellationToken))
        {
            return NotFound(new { error = $"Key '{clientId}' not found." });
        }

        bool revoked = await _credentialRegistry.RevokeKeyAsync(clientId, cancellationToken);
        if (!revoked)
        {
            return NotFound(new { error = $"Key '{clientId}' not found." });
        }

        return Ok(new { message = "Key revoked.", client_id = clientId, revoked_at = DateTime.UtcNow });
    }

    private async Task<bool> OwnsCredentialAsync(string clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CurrentAccountId))
        {
            return false;
        }

        var credential = await _credentialRegistry.GetClientByIdAsync(clientId, cancellationToken);
        return credential != null && string.Equals(credential.OwnerAccountId, CurrentAccountId, StringComparison.Ordinal);
    }
}
