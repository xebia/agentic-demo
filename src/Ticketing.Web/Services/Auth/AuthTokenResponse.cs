namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Response from the auth service token endpoint.
/// </summary>
public class AuthTokenResponse
{
    /// <summary>
    /// The JWT access token.
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// Token type (always "Bearer").
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// Expiration time in seconds from now.
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// The exact expiration timestamp.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Information about the authenticated principal.
    /// </summary>
    public required AuthTokenSubject Subject { get; set; }
}

/// <summary>
/// Information about the authenticated user from the token response.
/// </summary>
public class AuthTokenSubject
{
    /// <summary>
    /// The unique identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Assigned roles.
    /// </summary>
    public required string[] Roles { get; set; }
}
