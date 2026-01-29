using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Ticketing.Web.Services.Auth;

namespace Ticketing.Web.Middleware;

/// <summary>
/// Middleware that automatically signs in the default Help Desk user
/// when no user is authenticated. This ensures the demo app always has
/// an authenticated user without requiring manual login.
/// </summary>
public class AutoSignInMiddleware
{
    private readonly RequestDelegate _next;

    public AutoSignInMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if already authenticated or if this is the switch-user endpoint
        if (context.User?.Identity?.IsAuthenticated != true 
            && !context.Request.Path.StartsWithSegments("/auth/switch-user"))
        {
            // Sign in as the default Help Desk user
            var user = MockUser.DemoUsers.HelpDeskUser;
            
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Name, user.DisplayName)
            };
            
            foreach (var role in user.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "MockAuth");
            var principal = new ClaimsPrincipal(identity);

            await context.SignInAsync("MockAuth", principal);
            
            // Update the current request's user so it's available immediately
            context.User = principal;
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method to register the middleware.
/// </summary>
public static class AutoSignInMiddlewareExtensions
{
    public static IApplicationBuilder UseAutoSignIn(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AutoSignInMiddleware>();
    }
}
