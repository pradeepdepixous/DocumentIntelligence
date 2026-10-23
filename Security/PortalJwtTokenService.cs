using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Security;

/// <summary>
/// Issues signed session JWTs for the customer portal after a successful Google sign-in.
/// Separate from API keys: this token authenticates "who is using the portal", not
/// "which project may call the document-processing API".
/// </summary>
public class PortalJwtTokenService
{
    private readonly PortalAuthOptions _options;

    public PortalJwtTokenService(PortalAuthOptions options)
    {
        _options = options;
    }

    public string GenerateToken(CustomerAccountEntity account)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.AccountId),
            new(ClaimTypes.NameIdentifier, account.AccountId),
            new("account_id", account.AccountId),
            new(ClaimTypes.Email, account.Email),
            new(ClaimTypes.Name, account.Name),
        };

        if (account.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "admin"));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.JwtExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
