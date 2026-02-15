using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.OperationsAgent.Models;

namespace Ticketing.OperationsAgent.Services;

/// <summary>
/// Manages authentication with the central auth service using client credentials.
/// Caches the JWT token and refreshes it before expiration.
/// </summary>
public class AuthTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthTokenProvider> _logger;
    private readonly string _authServiceUrl;
    private readonly string _clientId;
    private readonly string _clientSecret;

    private string? _cachedToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public AuthTokenProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AuthTokenProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _authServiceUrl = configuration["AuthService:Url"]
            ?? throw new InvalidOperationException("AuthService:Url is not configured");
        _clientId = configuration["AuthService:ClientId"]
            ?? throw new InvalidOperationException("AuthService:ClientId is not configured");
        _clientSecret = configuration["AuthService:ClientSecret"]
            ?? throw new InvalidOperationException("AuthService:ClientSecret is not configured");
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && DateTime.UtcNow.AddMinutes(5) < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken != null && DateTime.UtcNow.AddMinutes(5) < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            _logger.LogInformation("Requesting new token from auth service for client {ClientId}", _clientId);

            var response = await _httpClient.PostAsJsonAsync(
                $"{_authServiceUrl}/token/client-credentials",
                new { clientId = _clientId, clientSecret = _clientSecret },
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Failed to deserialize auth token response");

            _cachedToken = tokenResponse.Token;
            _tokenExpiresAt = tokenResponse.ExpiresAt;

            _logger.LogInformation("Obtained new token, expires at {ExpiresAt:u}", _tokenExpiresAt);

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
