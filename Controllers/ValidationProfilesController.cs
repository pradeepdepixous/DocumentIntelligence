using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Application.Contracts.Validation;
using PixousDocIntelligence.Application.Validation;
using PixousDocIntelligence.Security;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Reusable Validation Profile management endpoints.
/// Allows client applications to configure, version, and maintain document validation criteria.
/// </summary>
[ApiController]
[Route("api/validation/profiles")]
public class ValidationProfilesController : ControllerBase
{
    private readonly IValidationProfileService _profileService;
    private readonly ILogger<ValidationProfilesController> _logger;

    public ValidationProfilesController(
        IValidationProfileService profileService,
        ILogger<ValidationProfilesController> logger)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new reusable validation profile for this tenant.
    /// </summary>
    [HttpPost]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
    [ProducesResponseType(typeof(ValidationProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateProfile(
        [FromBody] CreateValidationProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) return BadRequest(new { error = "Request body cannot be null." });

        string tenantId = ResolveTenantId();
        try
        {
            var profile = await _profileService.CreateProfileAsync(request, tenantId, cancellationToken);
            return CreatedAtAction(nameof(GetProfileById), new { id = profile.ProfileId }, profile);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lists all validation profiles available to this tenant.
    /// </summary>
    [HttpGet]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
    [ProducesResponseType(typeof(List<ValidationProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProfiles(CancellationToken cancellationToken = default)
    {
        string tenantId = ResolveTenantId();
        var profiles = await _profileService.ListProfilesAsync(tenantId, cancellationToken);
        return Ok(profiles);
    }

    /// <summary>
    /// Retrieves a validation profile by ID.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
    [ProducesResponseType(typeof(ValidationProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfileById(
        string id,
        CancellationToken cancellationToken = default)
    {
        string tenantId = ResolveTenantId();
        var profile = await _profileService.GetProfileAsync(id, tenantId, cancellationToken);
        if (profile == null)
        {
            return NotFound(new { error = $"Validation profile '{id}' not found." });
        }

        return Ok(profile);
    }

    /// <summary>
    /// Updates an existing validation profile and automatically increments its version.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(AuthenticationSchemes = PortalAuthSchemes.Combined)]
    [ProducesResponseType(typeof(ValidationProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        string id,
        [FromBody] UpdateValidationProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) return BadRequest(new { error = "Request body cannot be null." });

        string tenantId = ResolveTenantId();
        var updated = await _profileService.UpdateProfileAsync(id, request, tenantId, cancellationToken);
        if (updated == null)
        {
            return NotFound(new { error = $"Validation profile '{id}' not found." });
        }

        return Ok(updated);
    }

    private string ResolveTenantId()
    {
        return User.FindFirst("account_id")?.Value
            ?? User.Identity?.Name
            ?? Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? "default-tenant";
    }
}

