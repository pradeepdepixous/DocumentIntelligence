using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Persistence.Entities;
using PixousDocIntelligence.Security;
using PixousDocIntelligence.Services.Accounts;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Customer portal authentication: email+password registration/login, and Google ID token
/// exchange — all three return a Pixous portal session JWT. This does NOT authenticate
/// document-processing API calls; those still use X-Document-Api-Key.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly PortalAuthOptions _options;
    private readonly CustomerAccountService _accountService;
    private readonly PortalJwtTokenService _tokenService;

    public AuthController(PortalAuthOptions options, CustomerAccountService accountService, PortalJwtTokenService tokenService)
    {
        _options = options;
        _accountService = accountService;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Creates a new account with an email and password.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.JwtSigningKey))
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Portal authentication is not configured on this server (PortalAuth:JwtSigningKey)." });
        }

        var (success, account, error) = await _accountService.RegisterWithPasswordAsync(
            request.Email, request.Password, request.Name ?? string.Empty, cancellationToken);

        if (!success || account == null)
        {
            return BadRequest(new { error });
        }

        return Ok(BuildAuthResponse(account));
    }

    /// <summary>
    /// Signs in with an email and password. Locks the account for 15 minutes after 5 consecutive
    /// failed attempts, and never reveals whether the email exists if the attempt fails.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.JwtSigningKey))
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Portal authentication is not configured on this server (PortalAuth:JwtSigningKey)." });
        }

        var (success, account, error) = await _accountService.AuthenticateWithPasswordAsync(
            request.Email, request.Password, cancellationToken);

        if (!success || account == null)
        {
            int status = error != null && error.StartsWith("Too many failed attempts", StringComparison.Ordinal)
                ? StatusCodes.Status423Locked
                : StatusCodes.Status401Unauthorized;
            return StatusCode(status, new { error });
        }

        return Ok(BuildAuthResponse(account));
    }

    /// <summary>
    /// Verifies a Google ID token (from Google Identity Services on the frontend), creates or
    /// updates the matching portal account (linking it to an existing password account with the
    /// same email if one exists), and returns a Pixous session JWT.
    /// </summary>
    [HttpPost("google")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return BadRequest(new { error = "idToken is required." });
        }

        if (string.IsNullOrWhiteSpace(_options.GoogleClientId) || string.IsNullOrWhiteSpace(_options.JwtSigningKey))
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Google sign-in is not configured on this server (PortalAuth:GoogleClientId / PortalAuth:JwtSigningKey)." });
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _options.GoogleClientId }
            });
        }
        catch (InvalidJwtException ex)
        {
            return Unauthorized(new { error = "Invalid or expired Google token.", details = ex.Message });
        }

        var account = await _accountService.UpsertFromGoogleAsync(
            payload.Subject, payload.Email, payload.Name ?? payload.Email, payload.Picture, cancellationToken);

        return Ok(BuildAuthResponse(account));
    }

    private object BuildAuthResponse(CustomerAccountEntity account) => new
    {
        token = _tokenService.GenerateToken(account),
        account = new
        {
            account_id = account.AccountId,
            email = account.Email,
            name = account.Name,
            picture_url = account.PictureUrl,
            is_admin = account.IsAdmin
        }
    };
}

public class GoogleSignInRequest
{
    public string IdToken { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Name { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
