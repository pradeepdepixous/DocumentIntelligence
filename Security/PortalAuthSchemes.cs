namespace PixousDocIntelligence.Security;

/// <summary>
/// Authentication scheme names. <see cref="ApiKeyAuthenticationSchemeOptions.DefaultScheme"/>
/// authenticates external integrations calling with their own API key; <see cref="Bearer"/> is the
/// portal's JWT session scheme. <see cref="Combined"/> accepts either — used on the
/// document-processing/agent endpoints so a signed-in user can exercise them straight from the
/// Playground (spending their account's own credit) without first creating an API key, while an
/// external integration keeps using its key exactly as before.
/// </summary>
public static class PortalAuthSchemes
{
    public const string Bearer = "PortalBearer";

    public const string Combined = ApiKeyAuthenticationSchemeOptions.DefaultScheme + "," + Bearer;
}
