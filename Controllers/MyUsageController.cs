using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Credentials;
using PixousDocIntelligence.Security;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Usage summary for a signed-in portal customer, aggregated across every API key they own
/// (an account can have several keys/apps). Distinct from UsageController.GetMyUsage, which
/// reports on a single API key via the ApiKeyScheme, not a whole portal account via a JWT.
/// </summary>
[ApiController]
[Route("api/my/usage")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PortalAuthSchemes.Bearer)]
public class MyUsageController : ControllerBase
{
    private readonly ICredentialRegistry _credentialRegistry;

    public MyUsageController(ICredentialRegistry credentialRegistry)
    {
        _credentialRegistry = credentialRegistry;
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

        var keys = await _credentialRegistry.ListKeysByOwnerAsync(accountId, cancellationToken);

        return Ok(new
        {
            total_keys = keys.Count,
            active_keys = keys.Count(k => k.IsActive),
            total_pages_used_this_cycle = keys.Sum(k => k.PagesUsedThisMonth),
            total_requests = keys.Sum(k => k.TotalRequests),
            keys = keys.Select(k => new
            {
                client_id = k.ClientId,
                name = k.Name,
                key_prefix = k.KeyPrefix,
                permissions = k.Permissions,
                is_active = k.IsActive,
                plan_tier = k.PlanTier,
                monthly_page_quota = k.MonthlyPageQuota,
                pages_used_this_month = k.PagesUsedThisMonth,
                pages_remaining = Math.Max(0, k.MonthlyPageQuota - k.PagesUsedThisMonth),
                total_requests = k.TotalRequests,
                last_used_at = k.LastUsedAt
            })
        });
    }
}
