using System.Security.Cryptography;
using System.Text;

namespace PixousDocIntelligence.Credentials;

/// <summary>
/// Cryptographic utilities for generating secure project access keys and computing SHA-256 hashes.
/// </summary>
public static class KeyHasher
{
    private const string KeyPrefix = "pixous_sec_live_";

    /// <summary>
    /// Generates a cryptographically random access key with a standard prefix.
    /// Format: pixous_sec_live_[32 hex characters]
    /// </summary>
    public static string GenerateAccessKey()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(24);
        string hex = Convert.ToHexString(randomBytes).ToLowerInvariant();
        return $"{KeyPrefix}{hex}";
    }

    /// <summary>
    /// Computes the SHA-256 hash of an access key for secure zero-knowledge storage and lookup.
    /// </summary>
    public static string ComputeSha256Hash(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return string.Empty;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(rawKey.Trim());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Extracts a safe, masked key prefix for audit displays (e.g. 'pixous_sec_live_a1b2...').
    /// </summary>
    public static string ExtractPrefix(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return "pixous_...";
        return rawKey.Length > 24 ? $"{rawKey[..20]}..." : rawKey;
    }
}
