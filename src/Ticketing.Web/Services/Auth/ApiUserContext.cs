using System.Security.Claims;
using Ticketing.Domain.Services;

namespace Ticketing.Web.Services.Auth;

/// <summary>
/// User context for API requests that reads from JWT claims in HttpContext.
/// </summary>
public class ApiUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ApiUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CurrentUserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return "anonymous";
            }

            // For API requests with JWT, use the email claim (matches how tickets store CreatedBy)
            var email = user.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrEmpty(email))
            {
                return email;
            }

            // Fallback to NameIdentifier
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        }
    }

    public string? CurrentUserName
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // Try display_name claim first (from our JWT), then Name claim
            return user.FindFirst("display_name")?.Value 
                ?? user.FindFirst(ClaimTypes.Name)?.Value;
        }
    }

    /// <summary>
    /// Gets the roles of the current user.
    /// </summary>
    public IEnumerable<string> CurrentUserRoles
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return [];
            }

            return user.FindAll(ClaimTypes.Role).Select(c => c.Value);
        }
    }

    /// <summary>
    /// Checks if the current user is in the specified role.
    /// </summary>
    public bool IsInRole(string role)
    {
        return _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
    }

    /// <summary>
    /// Returns true if the user has elevated permissions (HelpDesk or Approver).
    /// Regular users can only see their own tickets.
    /// </summary>
    public bool HasElevatedAccess
    {
        get
        {
            return IsInRole(UserRoles.HelpDesk) || IsInRole(UserRoles.Approver);
        }
    }
}
