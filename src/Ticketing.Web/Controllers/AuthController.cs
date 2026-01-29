using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Web.Services.Auth;

namespace Ticketing.Web.Controllers;

/// <summary>
/// Authentication controller for obtaining JWT tokens.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _tokenService;

    public AuthController(JwtTokenService tokenService)
    {
        _tokenService = tokenService;
    }

    /// <summary>
    /// Get a JWT token for API authentication.
    /// Pass either a user ID or email to authenticate as that demo user.
    /// </summary>
    /// <remarks>
    /// This is a demo authentication endpoint. In a real system, this would
    /// validate credentials against a user store.
    /// 
    /// Available demo users:
    /// - helpdesk-user-1 / sarah.helpdesk@company.com (HelpDesk role)
    /// - approver-1 / mike.manager@company.com (Approver role)
    /// - admin-1 / admin@company.com (HelpDesk + Approver roles)
    /// - requestor-1 / john.employee@company.com (User role - sees only own tickets)
    /// </remarks>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<TokenResponse> GetToken([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) && string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "Either 'userId' or 'email' must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Find the user by ID or email
        var user = MockUser.DemoUsers.All.FirstOrDefault(u =>
            (!string.IsNullOrWhiteSpace(request.UserId) && 
             u.Id.Equals(request.UserId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(request.Email) && 
             u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)));

        if (user == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "User not found",
                Detail = $"No demo user found with the specified ID or email. Available users: {string.Join(", ", MockUser.DemoUsers.All.Select(u => u.Id))}",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var response = _tokenService.GenerateToken(user);
        return Ok(response);
    }

    /// <summary>
    /// List all available demo users that can be used for authentication.
    /// </summary>
    [HttpGet("users")]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<DemoUserInfo>>(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<DemoUserInfo>> GetAvailableUsers()
    {
        var users = MockUser.DemoUsers.All.Select(u => new DemoUserInfo
        {
            Id = u.Id,
            Email = u.Email,
            DisplayName = u.DisplayName,
            Roles = u.Roles
        });

        return Ok(users);
    }
}

/// <summary>
/// Request body for obtaining a JWT token.
/// </summary>
public class TokenRequest
{
    /// <summary>
    /// The ID of the demo user to authenticate as.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The email of the demo user to authenticate as.
    /// </summary>
    public string? Email { get; set; }
}

/// <summary>
/// Information about an available demo user.
/// </summary>
public class DemoUserInfo
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string[] Roles { get; set; }
}
