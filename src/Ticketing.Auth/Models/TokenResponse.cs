namespace Ticketing.Auth.Models;

/// <summary>
/// Response from the token endpoint.
/// </summary>
public class TokenResponse
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
    public required TokenSubjectInfo Subject { get; set; }
}

/// <summary>
/// Information about the token subject (user or service account).
/// </summary>
public class TokenSubjectInfo
{
    /// <summary>
    /// The unique identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Email address (for users) or system identifier (for service accounts).
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

    /// <summary>
    /// Whether this is a service account.
    /// </summary>
    public bool IsServiceAccount { get; set; }
}
