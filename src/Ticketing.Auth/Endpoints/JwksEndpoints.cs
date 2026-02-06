using Microsoft.AspNetCore.Mvc;
using Ticketing.Auth.Services;

namespace Ticketing.Auth.Endpoints;

/// <summary>
/// JWKS (JSON Web Key Set) endpoint for public key distribution.
/// </summary>
public static class JwksEndpoints
{
    public static void MapJwksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/jwks.json", GetJwks)
            .WithTags("Discovery")
            .WithSummary("Get the JWKS (JSON Web Key Set)")
            .WithDescription("Returns the public keys used to validate tokens issued by this auth service.");
    }

    private static IResult GetJwks(
        [FromServices] RsaKeyService keyService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("Auth.JWKS");
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var jwks = keyService.GetJwks();

        logger.LogInformation("JWKS requested from {ClientIp} - returning {KeyCount} key(s) with kid: {KeyId}",
            clientIp,
            jwks.Keys.Count,
            keyService.KeyId);

        // Return the JWKS as a JSON object with proper content type
        return Results.Json(new
        {
            keys = jwks.Keys.Select(k => new
            {
                kty = k.Kty,
                use = k.Use,
                kid = k.Kid,
                alg = k.Alg,
                n = k.N,
                e = k.E
            })
        });
    }
}
