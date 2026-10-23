using Microsoft.AspNetCore.Authentication;

namespace PixousDocIntelligence.Security;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKeyScheme";
    public const string HeaderName = "X-Document-Api-Key";
}
