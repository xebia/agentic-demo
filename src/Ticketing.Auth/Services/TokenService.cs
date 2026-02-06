using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Ticketing.Auth.Models;

namespace Ticketing.Auth.Services;

/// <summary>
/// Service for generating RS256-signed JWT tokens.
/// </summary>
public class TokenService
{
    private readonly AuthSettings _settings;
    private readonly RsaKeyService _keyService;

    public TokenService(IOptions<AuthSettings> settings, RsaKeyService keyService)
    {
        _settings = settings.Value;
        _keyService = keyService;
    }

    /// <summary>
    /// Generates a JWT token for a user.
    /// </summary>
    public TokenResponse GenerateUserToken(MockUser user)
    {
        var claims = BuildClaims(user.Id, user.Email, user.DisplayName, user.Roles, isServiceAccount: false);
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.TokenLifetimeMinutes);

        return CreateToken(claims, expiresAt, new TokenSubjectInfo
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Roles = user.Roles,
            IsServiceAccount = false
        });
    }

    /// <summary>
    /// Generates a JWT token for a service account.
    /// </summary>
    public TokenResponse GenerateServiceAccountToken(ServiceAccount serviceAccount)
    {
        var id = $"{serviceAccount.ClientId}@system";
        var email = $"{serviceAccount.ClientId}@system.local";

        var claims = BuildClaims(id, email, serviceAccount.DisplayName, serviceAccount.Roles, isServiceAccount: true);
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.ServiceAccountTokenLifetimeMinutes);

        return CreateToken(claims, expiresAt, new TokenSubjectInfo
        {
            Id = id,
            Email = email,
            DisplayName = serviceAccount.DisplayName,
            Roles = serviceAccount.Roles,
            IsServiceAccount = true
        });
    }

    private List<Claim> BuildClaims(string id, string email, string displayName, string[] roles, bool isServiceAccount)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, displayName),
            new("display_name", displayName),
            new("is_service_account", isServiceAccount.ToString().ToLowerInvariant())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return claims;
    }

    private TokenResponse CreateToken(List<Claim> claims, DateTime expiresAt, TokenSubjectInfo subject)
    {
        var credentials = _keyService.GetSigningCredentials();

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var expiresIn = (int)(expiresAt - DateTime.UtcNow).TotalSeconds;

        return new TokenResponse
        {
            Token = tokenString,
            ExpiresIn = expiresIn,
            ExpiresAt = expiresAt,
            Subject = subject
        };
    }
}
