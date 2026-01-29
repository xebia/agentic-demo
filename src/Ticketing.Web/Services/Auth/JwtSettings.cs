namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Configuration settings for JWT token generation and validation.
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// Secret key used to sign JWT tokens.
    /// In production, this should come from secure configuration (Key Vault, etc.)
    /// </summary>
    public string SecretKey { get; set; } = "TicketingDemoSecretKey_AtLeast32Characters!";

    /// <summary>
    /// Token issuer (who created the token).
    /// </summary>
    public string Issuer { get; set; } = "TicketingDemo";

    /// <summary>
    /// Token audience (who the token is intended for).
    /// </summary>
    public string Audience { get; set; } = "TicketingDemoApi";

    /// <summary>
    /// Token expiration time in minutes.
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;
}
