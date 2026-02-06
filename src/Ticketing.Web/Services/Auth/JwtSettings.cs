namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Configuration settings for JWT token validation via auth service.
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// URL of the auth service (for JWKS endpoint).
    /// </summary>
    public string AuthServiceUrl { get; set; } = "http://localhost:5001";

    /// <summary>
    /// Token issuer (must match what auth service uses).
    /// </summary>
    public string Issuer { get; set; } = "https://auth.ticketing.local";

    /// <summary>
    /// Token audience (must match what auth service uses).
    /// </summary>
    public string Audience { get; set; } = "ticketing-api";
}
