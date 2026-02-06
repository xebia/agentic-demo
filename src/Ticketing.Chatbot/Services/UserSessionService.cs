using Ticketing.Chatbot.Models;

namespace Ticketing.Chatbot.Services;

/// <summary>
/// Manages the current user session state.
/// This is scoped per-circuit in Blazor Server.
/// </summary>
public class UserSessionService
{
    public DemoUser? CurrentUser { get; private set; }
    public string? AccessToken { get; private set; }
    public DateTime? TokenExpiresAt { get; private set; }

    public bool IsAuthenticated => CurrentUser != null && !string.IsNullOrEmpty(AccessToken);

    public event Action? OnUserChanged;

    public void SetUser(DemoUser user, TokenResponse token)
    {
        CurrentUser = user;
        AccessToken = token.Token;
        TokenExpiresAt = token.ExpiresAt;
        OnUserChanged?.Invoke();
    }

    public void ClearUser()
    {
        CurrentUser = null;
        AccessToken = null;
        TokenExpiresAt = null;
        OnUserChanged?.Invoke();
    }

    public bool IsTokenExpired()
    {
        if (TokenExpiresAt == null) return true;
        return DateTime.UtcNow >= TokenExpiresAt.Value.AddMinutes(-1); // 1 minute buffer
    }
}
