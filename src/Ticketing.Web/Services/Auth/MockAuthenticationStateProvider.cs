using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Mock authentication state provider for demo purposes.
/// Reads authentication state from HttpContext cookies (set via SSR pages).
/// </summary>
public class MockAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MockAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current user from HttpContext claims, or returns a default anonymous user.
    /// </summary>
    public MockUser CurrentUser
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(userId))
                {
                    var user = MockUser.DemoUsers.All.FirstOrDefault(u => u.Id == userId);
                    if (user != null)
                    {
                        return user;
                    }
                }
            }
            
            // Default to Help Desk user if not authenticated
            return MockUser.DemoUsers.HelpDeskUser;
        }
    }

    public IReadOnlyList<MockUser> AvailableUsers => MockUser.DemoUsers.All;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            // Return the authenticated user from HttpContext
            return Task.FromResult(new AuthenticationState(httpContext.User));
        }
        
        // Create default authentication state with Help Desk user
        var user = MockUser.DemoUsers.HelpDeskUser;
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            .. user.Roles.Select(r => new Claim(ClaimTypes.Role, r))
        ], "MockAuth");

        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(new AuthenticationState(principal));
    }

    /// <summary>
    /// Notifies that authentication state has changed.
    /// Note: User switching is now handled via SSR page navigation.
    /// </summary>
    public void NotifyStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
