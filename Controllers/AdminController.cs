using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Credentials;
using PixousDocIntelligence.Security;
using PixousDocIntelligence.Services.Accounts;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Cross-customer oversight for the allowlisted portal admin, reachable with just their Google
/// sign-in session (no separate admin API key needed). Distinct from CredentialsController/
/// UsageController, which remain the API-key-scheme equivalents used by Swagger/CLI tooling.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Bearer, Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly CustomerAccountService _accountService;
    private readonly ICredentialRegistry _credentialRegistry;

    public AdminController(CustomerAccountService accountService, ICredentialRegistry credentialRegistry)
    {
        _accountService = accountService;
        _credentialRegistry = credentialRegistry;
    }

    /// <summary>
    /// Every customer account, with their own keys and usage rolled up. Accounts with zero keys
    /// still appear (e.g. someone who signed in but hasn't created a key yet).
    /// </summary>
    [HttpGet("accounts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAccounts(CancellationToken cancellationToken)
    {
        var accounts = await _accountService.ListAllAsync(cancellationToken);
        var allKeys = await _credentialRegistry.ListKeysAsync(cancellationToken);
        var keysByOwner = allKeys
            .Where(k => !string.IsNullOrWhiteSpace(k.OwnerAccountId))
            .GroupBy(k => k.OwnerAccountId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Ok(accounts.Select(a =>
        {
            var keys = keysByOwner.TryGetValue(a.AccountId, out var k) ? k : new List<ClientCredential>();
            return new
            {
                account_id = a.AccountId,
                email = a.Email,
                name = a.Name,
                picture_url = a.PictureUrl,
                is_admin = a.IsAdmin,
                created_at = a.CreatedAt,
                last_login_at = a.LastLoginAt,
                credit_limit = a.CreditLimit,
                credits_used = a.CreditsUsed,
                credits_remaining = Math.Max(0, a.CreditLimit - a.CreditsUsed),
                total_keys = keys.Count,
                active_keys = keys.Count(c => c.IsActive),
                total_requests = keys.Sum(c => c.TotalRequests)
            };
        }));
    }

    /// <summary>
    /// A specific customer's own keys, in full (admin view) — same shape as their own "My API Access" list.
    /// </summary>
    [HttpGet("accounts/{accountId}/credentials")]
    [ProducesResponseType(typeof(IEnumerable<ClientCredential>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAccountCredentials(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountService.GetByIdAsync(accountId, cancellationToken);
        if (account == null)
        {
            return NotFound(new { error = $"Account '{accountId}' not found." });
        }

        var keys = await _credentialRegistry.ListKeysByOwnerAsync(accountId, cancellationToken);
        return Ok(keys);
    }

    /// <summary>
    /// Platform-wide usage summary across every key, regardless of owner — the JWT-session
    /// equivalent of UsageController.GetAllUsage.
    /// </summary>
    [HttpGet("usage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlatformUsage(CancellationToken cancellationToken)
    {
        var accounts = await _accountService.ListAllAsync(cancellationToken);
        var keys = await _credentialRegistry.ListKeysAsync(cancellationToken);

        return Ok(new
        {
            summary = new
            {
                total_accounts = accounts.Count,
                total_registered_keys = keys.Count,
                active_keys = keys.Count(k => k.IsActive),
                total_pages_processed_this_cycle = keys.Sum(k => k.PagesUsedThisMonth),
                total_api_requests = keys.Sum(k => k.TotalRequests)
            },
            keys = keys.Select(k => new
            {
                client_id = k.ClientId,
                owner_account_id = k.OwnerAccountId,
                name = k.Name,
                key_prefix = k.KeyPrefix,
                plan_tier = k.PlanTier,
                monthly_quota = k.MonthlyPageQuota,
                pages_used = k.PagesUsedThisMonth,
                total_requests = k.TotalRequests,
                is_active = k.IsActive,
                last_used_at = k.LastUsedAt
            })
        });
    }

    /// <summary>
    /// Grants or adjusts a customer account's shared credit limit (the pool spent by both their
    /// own Playground testing and every self-serve key they own).
    /// </summary>
    [HttpPost("accounts/{accountId}/credit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAccountCredit(string accountId, [FromBody] SetAccountCreditRequest request, CancellationToken cancellationToken)
    {
        if (request.CreditLimit < 0)
        {
            return BadRequest(new { error = "creditLimit must be zero or positive." });
        }

        var updated = await _accountService.SetCreditLimitAsync(accountId, request.CreditLimit, cancellationToken);
        if (updated == null)
        {
            return NotFound(new { error = $"Account '{accountId}' not found." });
        }

        return Ok(new
        {
            message = $"Credit limit updated for '{updated.Email}'.",
            account_id = updated.AccountId,
            credit_limit = updated.CreditLimit,
            credits_used = updated.CreditsUsed,
            credits_remaining = Math.Max(0, updated.CreditLimit - updated.CreditsUsed)
        });
    }

    /// <summary>
    /// Revokes any customer's key immediately, regardless of who owns it — the admin moderation action.
    /// </summary>
    [HttpPost("credentials/{clientId}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAnyCredential(string clientId, CancellationToken cancellationToken)
    {
        bool revoked = await _credentialRegistry.RevokeKeyAsync(clientId, cancellationToken);
        if (!revoked)
        {
            return NotFound(new { error = $"Key '{clientId}' not found." });
        }

        return Ok(new { message = $"Key '{clientId}' revoked by admin.", client_id = clientId, revoked_at = DateTime.UtcNow });
    }
}

public class SetAccountCreditRequest
{
    public int CreditLimit { get; set; }
}
