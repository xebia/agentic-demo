using Ticketing.Domain.Services;

namespace Ticketing.Web.Services.Auth;

/// <summary>
/// User context that reads from the mock authentication state provider.
/// </summary>
public class BlazorUserContext : IUserContext
{
    private readonly MockAuthenticationStateProvider _authProvider;

    public BlazorUserContext(MockAuthenticationStateProvider authProvider)
    {
        _authProvider = authProvider;
    }

    public string CurrentUserId => _authProvider.CurrentUser.Id;
    
    public string CurrentUserName => _authProvider.CurrentUser.DisplayName;
}
