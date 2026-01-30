namespace Ticketing.Auth.Models;

/// <summary>
/// Configuration settings for the auth service.
/// </summary>
public class AuthSettings
{
    /// <summary>
    /// Token issuer (who created the token).
    /// </summary>
    public string Issuer { get; set; } = "https://auth.ticketing.local";

    /// <summary>
    /// Token audience (who the token is intended for).
    /// </summary>
    public string Audience { get; set; } = "ticketing-api";

    /// <summary>
    /// Token expiration time in minutes for user tokens.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Token expiration time in minutes for service account tokens.
    /// </summary>
    public int ServiceAccountTokenLifetimeMinutes { get; set; } = 30;
}
