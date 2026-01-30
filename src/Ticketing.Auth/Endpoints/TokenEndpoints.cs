using Microsoft.AspNetCore.Mvc;
using Ticketing.Auth.Models;
using Ticketing.Auth.Services;

namespace Ticketing.Auth.Endpoints;

/// <summary>
/// Token endpoint mappings.
/// </summary>
public static class TokenEndpoints
{
    public static void MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("")
            .WithTags("Authentication");

        // User token endpoint
        group.MapPost("/token", GetUserToken)
            .WithSummary("Get a JWT token for a user")
            .WithDescription("Returns a JWT token for the specified user (by ID or email). This is a demo endpoint - no password required.");

        // Service account token endpoint (client credentials)
        group.MapPost("/token/client-credentials", GetServiceAccountToken)
            .WithSummary("Get a JWT token for a service account")
            .WithDescription("Returns a JWT token for a service account using client credentials flow.");

        // List available users
        group.MapGet("/users", GetAvailableUsers)
            .WithSummary("List available demo users")
            .WithDescription("Returns a list of all demo users that can be used for authentication.");
    }

    private static IResult GetUserToken(
        [FromBody] TokenRequest request,
        [FromServices] IdentityStore identityStore,
        [FromServices] TokenService tokenService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("Auth.Token");
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        logger.LogInformation("Token request received from {ClientIp} for user: {UserId} / {Email}",
            clientIp,
            request.UserId ?? "(not provided)",
            request.Email ?? "(not provided)");

        if (string.IsNullOrWhiteSpace(request.UserId) && string.IsNullOrWhiteSpace(request.Email))
        {
            logger.LogWarning("Token request rejected: neither userId nor email provided");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "Either 'userId' or 'email' must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var user = identityStore.FindUser(request.UserId, request.Email);

        if (user == null)
        {
            logger.LogWarning("Token request failed: user not found for {UserId} / {Email}",
                request.UserId ?? "(not provided)",
                request.Email ?? "(not provided)");

            var availableUsers = string.Join(", ", identityStore.GetAllUsers().Select(u => u.Id));
            return Results.BadRequest(new ProblemDetails
            {
                Title = "User not found",
                Detail = $"No user found with the specified ID or email. Available users: {availableUsers}",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var response = tokenService.GenerateUserToken(user);

        logger.LogInformation("Token issued for user {UserId} ({DisplayName}) with roles [{Roles}], expires in {ExpiresIn}s",
            user.Id,
            user.DisplayName,
            string.Join(", ", user.Roles),
            response.ExpiresIn);

        return Results.Ok(response);
    }

    private static IResult GetServiceAccountToken(
        [FromBody] ClientCredentialsRequest request,
        [FromServices] IdentityStore identityStore,
        [FromServices] TokenService tokenService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("Auth.ClientCredentials");
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        logger.LogInformation("Client credentials request received from {ClientIp} for client: {ClientId}",
            clientIp,
            request.ClientId);

        var serviceAccount = identityStore.ValidateServiceAccount(request.ClientId, request.ClientSecret);

        if (serviceAccount == null)
        {
            logger.LogWarning("Client credentials rejected for client: {ClientId} - invalid credentials",
                request.ClientId);
            return Results.Unauthorized();
        }

        var response = tokenService.GenerateServiceAccountToken(serviceAccount);

        logger.LogInformation("Token issued for service account {ClientId} ({DisplayName}) with roles [{Roles}], expires in {ExpiresIn}s",
            serviceAccount.ClientId,
            serviceAccount.DisplayName,
            string.Join(", ", serviceAccount.Roles),
            response.ExpiresIn);

        return Results.Ok(response);
    }

    private static IResult GetAvailableUsers(
        [FromServices] IdentityStore identityStore,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("Auth.Users");
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var users = identityStore.GetAllUsers().Select(u => new
        {
            u.Id,
            u.Email,
            u.DisplayName,
            u.Roles
        }).ToList();

        logger.LogInformation("Users list requested from {ClientIp}, returning {Count} users",
            clientIp,
            users.Count);

        return Results.Ok(users);
    }
}
