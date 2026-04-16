using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Mock authentication state provider for demo purposes.
/// Inherits from <see cref="ServerAuthenticationStateProvider"/> so the Blazor framework
/// flows the authenticated principal into the provider for both the SSR render and the
/// interactive circuit. Relying on <see cref="IHttpContextAccessor"/> directly doesn't
/// work once the circuit is live, because HttpContext is only available during the
/// initial HTTP request.
/// </summary>
public class MockAuthenticationStateProvider : ServerAuthenticationStateProvider
{
    /// <summary>
    /// Gets the current user from the cached authentication state, or the default Help Desk user if not authenticated.
    /// </summary>
    public MockUser CurrentUser
    {
        get
        {
            try
            {
                var stateTask = GetAuthenticationStateAsync();
                if (stateTask.IsCompletedSuccessfully)
                {
                    var principal = stateTask.Result.User;
                    if (principal?.Identity?.IsAuthenticated == true)
                    {
                        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (!string.IsNullOrEmpty(userId))
                        {
                            var user = MockUser.DemoUsers.All.FirstOrDefault(u => u.Id == userId);
                            if (user != null)
                            {
                                return user;
                            }
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // State hasn't been set yet (framework hasn't populated it).
            }

            return MockUser.DemoUsers.HelpDeskUser;
        }
    }

    public IReadOnlyList<MockUser> AvailableUsers => MockUser.DemoUsers.All;

    /// <summary>
    /// Notifies that authentication state has changed.
    /// </summary>
    public void NotifyStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
