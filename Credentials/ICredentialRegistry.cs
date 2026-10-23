namespace PixousDocIntelligence.Credentials;

/// <summary>
/// Central registry interface for managing project access keys, hashing, rotation, and revocation.
/// </summary>
public interface ICredentialRegistry
{
    /// <summary>
    /// Validates an incoming raw access key by computing its hash and checking active/expiry status.
    /// Returns the matched ClientCredential if valid, null otherwise.
    /// </summary>
    Task<ClientCredential?> ValidateKeyAsync(string rawKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new unique project access key, hashes it, and stores the record in the registry.
    /// The plaintext key is returned ONLY once upon creation.
    /// </summary>
    Task<(ClientCredential Credential, string PlaintextKey)> CreateKeyAsync(
        string name,
        List<string> permissions,
        DateTime? expiresAt = null,
        string? ownerAccountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an existing client credential, immediately denying further API requests.
    /// </summary>
    Task<bool> RevokeKeyAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a project access key by generating a new key, updating the hash, and returning the new plaintext key.
    /// </summary>
    Task<(ClientCredential Credential, string PlaintextKey)?> RotateKeyAsync(
        string clientId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a client credential record by its ClientId.
    /// </summary>
    Task<ClientCredential?> GetClientByIdAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the client has sufficient monthly quota remaining for incoming pages.
    /// </summary>
    Task<(bool Allowed, int RemainingPages, string Message)> CheckQuotaAsync(
        string clientId, 
        int incomingPages, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records billable usage (e.g. processed pages or queries) and increments the client's usage counters.
    /// </summary>
    Task RecordUsageAsync(
        string clientId, 
        int pagesProcessed, 
        string operation, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all registered projects with their masked prefixes and permission status.
    /// </summary>
    Task<IReadOnlyList<ClientCredential>> ListKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists only the keys self-serve created by a given portal account (owner_account_id match).
    /// </summary>
    Task<IReadOnlyList<ClientCredential>> ListKeysByOwnerAsync(string ownerAccountId, CancellationToken cancellationToken = default);
}
