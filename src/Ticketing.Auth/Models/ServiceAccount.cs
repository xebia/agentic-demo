namespace Ticketing.Auth.Models;

/// <summary>
/// Configuration for a service account that can authenticate via client credentials.
/// </summary>
public class ServiceAccount
{
    /// <summary>
    /// Unique identifier for the service account.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Secret used to authenticate the service account.
    /// In production, this should be hashed.
    /// </summary>
    public required string ClientSecret { get; set; }

    /// <summary>
    /// Human-readable name for the service account.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Roles assigned to the service account.
    /// </summary>
    public string[] Roles { get; set; } = [];
}
