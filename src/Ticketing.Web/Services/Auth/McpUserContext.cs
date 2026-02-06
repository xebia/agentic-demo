using System.Security.Claims;
using Ticketing.Domain.Services;

namespace Ticketing.Web.Services.Auth;

/// <summary>
/// User context for MCP requests that reads from JWT claims in HttpContext.
/// Supports user impersonation for LLM agents acting on behalf of users.
/// </summary>
public class McpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public McpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string CurrentUserId
    {
        get
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return "anonymous";
            }

            // For MCP requests with JWT, use the email claim (matches how tickets store CreatedBy)
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrEmpty(email))
            {
                return email;
            }

            // Fallback to NameIdentifier
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        }
    }

    public string? CurrentUserName
    {
        get
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // Try display_name claim first (from our JWT), then Name claim
            return User.FindFirst("display_name")?.Value 
                ?? User.FindFirst(ClaimTypes.Name)?.Value;
        }
    }

    /// <summary>
    /// Gets the roles of the current user.
    /// </summary>
    public IEnumerable<string> CurrentUserRoles
    {
        get
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return [];
            }

            return User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        }
    }

    /// <summary>
    /// Checks if the current user is in the specified role.
    /// </summary>
    public bool IsInRole(string role)
    {
        return User?.IsInRole(role) ?? false;
    }

    /// <summary>
    /// Returns true if the user has elevated permissions (HelpDesk or Approver).
    /// Regular users can only see their own tickets.
    /// </summary>
    public bool HasElevatedAccess => IsInRole(UserRoles.HelpDesk) || IsInRole(UserRoles.Approver);

    /// <summary>
    /// Gets whether the current request is authenticated.
    /// </summary>
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
