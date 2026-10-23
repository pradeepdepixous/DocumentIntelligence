using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Credentials;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Administrative controller for managing multi-project Document Intelligence access credentials.
/// Enables secure key provisioning, rotation, revocation, and zero-knowledge audits.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Roles = "admin")]
public class CredentialsController : ControllerBase
{
    private readonly ICredentialRegistry _credentialRegistry;

    public CredentialsController(ICredentialRegistry credentialRegistry)
    {
        _credentialRegistry = credentialRegistry;
    }

    /// <summary>
    /// Lists all registered consuming projects with their active status, permissions, and masked key prefixes.
    /// (Never returns full access keys or hashes).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ClientCredential>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCredentials(CancellationToken cancellationToken)
    {
        var list = await _credentialRegistry.ListKeysAsync(cancellationToken);
        return Ok(list);
    }

    /// <summary>
    /// Provisions a new Document Intelligence access key for a consuming project.
    /// IMPORTANT: The plaintext key is returned ONLY once in this response. Store it securely in the calling project's secrets vault.
    /// </summary>
    /// <param name="request">Project name and permission configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCredential(
        [FromBody] CreateCredentialRequest request, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Project name is required." });
        }

        var (credential, plaintextKey) = await _credentialRegistry.CreateKeyAsync(
            request.Name,
            request.Permissions ?? new List<string> { "process", "summarize", "ask", "evaluate" },
            request.ExpiresAt,
            ownerAccountId: null,
            cancellationToken: cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "Access key provisioned successfully. Save this secret key now; it cannot be recovered.",
            client_id = credential.ClientId,
            project_name = credential.Name,
            access_key = plaintextKey,
            key_prefix = credential.KeyPrefix,
            permissions = credential.Permissions,
            created_at = credential.CreatedAt,
            expires_at = credential.ExpiresAt
        });
    }

    /// <summary>
    /// Rotates a project's access key. The old key is invalidated immediately and a new key is issued.
    /// </summary>
    /// <param name="clientId">The client ID of the project to rotate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{clientId}/rotate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateCredential(string clientId, CancellationToken cancellationToken)
    {
        var result = await _credentialRegistry.RotateKeyAsync(clientId, cancellationToken);
        if (result == null)
        {
            return NotFound(new { error = $"Client with ID '{clientId}' not found." });
        }

        return Ok(new
        {
            message = "Access key rotated successfully. Update your consuming application with this new key immediately.",
            client_id = result.Value.Credential.ClientId,
            project_name = result.Value.Credential.Name,
            new_access_key = result.Value.PlaintextKey,
            key_prefix = result.Value.Credential.KeyPrefix,
            rotated_at = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Revokes a project's access key immediately, blocking all subsequent requests from that client.
    /// </summary>
    /// <param name="clientId">The client ID of the project to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpDelete("{clientId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeCredential(string clientId, CancellationToken cancellationToken)
    {
        bool revoked = await _credentialRegistry.RevokeKeyAsync(clientId, cancellationToken);
        if (!revoked)
        {
            return NotFound(new { error = $"Client with ID '{clientId}' not found." });
        }

        return Ok(new
        {
            message = $"Client '{clientId}' access key has been revoked successfully. Requests will be rejected.",
            client_id = clientId,
            revoked_at = DateTime.UtcNow
        });
    }
}

/// <summary>
/// Payload for provisioning a new client project credential.
/// </summary>
public class CreateCredentialRequest
{
    /// <summary>
    /// Human-readable name of the consuming project (e.g., 'Invoice Management System').
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of permitted operations: 'process', 'summarize', 'ask', 'evaluate', 'admin'.
    /// </summary>
    public List<string>? Permissions { get; set; }

    /// <summary>
    /// Optional expiration timestamp.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
