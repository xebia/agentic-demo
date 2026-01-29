namespace Ticketing.Domain.Services;

/// <summary>
/// Provides context about the current user for business operations.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Gets the identifier (email) of the current user.
    /// </summary>
    string CurrentUserId { get; }

    /// <summary>
    /// Gets the display name of the current user.
    /// </summary>
    string? CurrentUserName { get; }
}

/// <summary>
/// Default implementation that can be configured from DI.
/// </summary>
public class UserContext : IUserContext
{
    public string CurrentUserId { get; set; } = "system@ticketing.local";
    public string? CurrentUserName { get; set; }
}
