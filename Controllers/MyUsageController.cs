using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Credentials;
using PixousDocIntelligence.Security;
using PixousDocIntelligence.Services.Accounts;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Usage summary for a signed-in portal customer: the account's shared credit pool (the real
/// enforcement limit — spent by Playground testing and every self-serve key alike), plus a
/// per-key breakdown for display. Distinct from UsageController.GetMyUsage, which reports on a
/// single API key via the ApiKeyScheme, not a whole portal account via a JWT.
/// </summary>
[ApiController]
[Route("api/my/usage")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Bearer)]
public class MyUsageController : ControllerBase
{
    private readonly ICredentialRegistry _credentialRegistry;
    private readonly CustomerAccountService _accountService;

    public MyUsageController(ICredentialRegistry credentialRegistry, CustomerAccountService accountService)
    {
        _credentialRegistry = credentialRegistry;
        _accountService = accountService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyUsage(CancellationToken cancellationToken)
    {
        string? accountId = User.FindFirst("account_id")?.Value;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Unauthorized(new { error = "Unable to determine portal account from session." });
        }

        var account = await _accountService.GetByIdAsync(accountId, cancellationToken);
        if (account == null)
        {
            return NotFound(new { error = $"Account '{accountId}' not found." });
        }

        var keys = await _credentialRegistry.ListKeysByOwnerAsync(accountId, cancellationToken);

        return Ok(new
        {
            credit = new
            {
                credit_limit = account.CreditLimit,
                credits_used = account.CreditsUsed,
                credits_remaining = Math.Max(0, account.CreditLimit - account.CreditsUsed),
                billing_cycle = account.BillingCycle
            },
            total_keys = keys.Count,
            active_keys = keys.Count(k => k.IsActive),
            total_requests = keys.Sum(k => k.TotalRequests),
            keys = keys.Select(k => new
            {
                client_id = k.ClientId,
                name = k.Name,
                key_prefix = k.KeyPrefix,
                permissions = k.Permissions,
                is_active = k.IsActive,
                pages_used_this_month = k.PagesUsedThisMonth,
                total_requests = k.TotalRequests,
                last_used_at = k.LastUsedAt
            })
        });
    }
}
