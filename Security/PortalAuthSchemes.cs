namespace PixousDocIntelligence.Security;

/// <summary>
/// Authentication scheme names. The API-key scheme (<see cref="ApiKeyAuthenticationSchemeOptions.DefaultScheme"/>)
/// remains the default for document-processing endpoints; <see cref="Bearer"/> is the portal's
/// separate JWT session scheme used only by customer-facing "my ..." endpoints.
/// </summary>
public static class PortalAuthSchemes
{
    public const string Bearer = "PortalBearer";
}
