namespace Ticketing.Auth.Models;

/// <summary>
/// Request body for obtaining a user JWT token.
/// </summary>
public class TokenRequest
{
    /// <summary>
    /// The ID of the user to authenticate as.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The email of the user to authenticate as.
    /// </summary>
    public string? Email { get; set; }
}
