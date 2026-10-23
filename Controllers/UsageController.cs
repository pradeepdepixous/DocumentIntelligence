using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Credentials;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Endpoints for tracking billable document processing metrics, page quotas, and client usage summaries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class UsageController : ControllerBase
{
    private readonly ICredentialRegistry _credentialRegistry;

    public UsageController(ICredentialRegistry credentialRegistry)
    {
        _credentialRegistry = credentialRegistry;
    }

    /// <summary>
    /// Retrieves live usage metrics, monthly page quotas, and remaining balance for the calling authenticated client project.
    /// Can be embedded into client customer portals and billing dashboards.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyUsage(CancellationToken cancellationToken)
    {
        string? clientId = User.FindFirst("client_id")?.Value;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Unauthorized(new { error = "Unable to determine client identity from authentication context." });
        }

        var client = await _credentialRegistry.GetClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return NotFound(new { error = $"Client '{clientId}' not found in registry." });
        }

        int remaining = Math.Max(0, client.MonthlyPageQuota - client.PagesUsedThisMonth);
        double percentUsed = client.MonthlyPageQuota > 0 
            ? Math.Round(((double)client.PagesUsedThisMonth / client.MonthlyPageQuota) * 100, 2) 
            : 0.0;

        return Ok(new
        {
            client_id = client.ClientId,
            project_name = client.Name,
            plan_tier = client.PlanTier,
            billing_cycle = client.BillingCycle,
            usage = new
            {
                monthly_page_quota = client.MonthlyPageQuota,
                pages_used_this_month = client.PagesUsedThisMonth,
                pages_remaining = remaining,
                total_requests = client.TotalRequests,
                percent_quota_used = percentUsed
            },
            status = client.IsActive ? "Active" : "Revoked",
            created_at = client.CreatedAt,
            last_used_at = client.LastUsedAt
        });
    }

    /// <summary>
    /// Administrative overview of usage, quotas, and throughput across all consuming client projects.
    /// </summary>
    [HttpGet("all")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllUsage(CancellationToken cancellationToken)
    {
        var clients = await _credentialRegistry.ListKeysAsync(cancellationToken);

        int totalPages = clients.Sum(c => c.PagesUsedThisMonth);
        long totalRequests = clients.Sum(c => c.TotalRequests);
        int activeClients = clients.Count(c => c.IsActive);

        return Ok(new
        {
            summary = new
            {
                total_registered_clients = clients.Count,
                active_clients = activeClients,
                total_pages_processed_this_cycle = totalPages,
                total_api_requests = totalRequests
            },
            clients = clients.Select(c => new
            {
                client_id = c.ClientId,
                name = c.Name,
                plan_tier = c.PlanTier,
                key_prefix = c.KeyPrefix,
                monthly_quota = c.MonthlyPageQuota,
                pages_used = c.PagesUsedThisMonth,
                pages_remaining = Math.Max(0, c.MonthlyPageQuota - c.PagesUsedThisMonth),
                total_requests = c.TotalRequests,
                is_active = c.IsActive,
                last_used_at = c.LastUsedAt
            })
        });
    }
}
