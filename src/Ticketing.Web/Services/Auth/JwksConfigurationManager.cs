using System.Net.Http.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Custom configuration manager that retrieves signing keys from a JWKS endpoint.
/// This is used when the auth service only provides a JWKS endpoint (not full OIDC discovery).
/// </summary>
public class JwksConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly string _jwksUrl;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(30);
    private readonly TimeSpan _automaticRefreshInterval = TimeSpan.FromHours(24);

    private OpenIdConnectConfiguration? _currentConfiguration;
    private DateTime _lastRefresh = DateTime.MinValue;

    public JwksConfigurationManager(string jwksUrl, HttpClient? httpClient = null)
    {
        _jwksUrl = jwksUrl;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        if (_currentConfiguration != null &&
            DateTime.UtcNow - _lastRefresh < _automaticRefreshInterval)
        {
            return _currentConfiguration;
        }

        return await RefreshConfigurationAsync(cancel);
    }

    public void RequestRefresh()
    {
        // Force refresh on next GetConfigurationAsync call
        _lastRefresh = DateTime.MinValue;
    }

    private async Task<OpenIdConnectConfiguration> RefreshConfigurationAsync(CancellationToken cancel)
    {
        try
        {
            var response = await _httpClient.GetAsync(_jwksUrl, cancel);
            response.EnsureSuccessStatusCode();

            var jwksJson = await response.Content.ReadAsStringAsync(cancel);
            var jwks = new JsonWebKeySet(jwksJson);

            var configuration = new OpenIdConnectConfiguration();
            foreach (var key in jwks.Keys)
            {
                configuration.SigningKeys.Add(key);
            }

            _currentConfiguration = configuration;
            _lastRefresh = DateTime.UtcNow;

            return configuration;
        }
        catch (Exception ex)
        {
            // If we have a cached configuration, return it on error
            if (_currentConfiguration != null)
            {
                return _currentConfiguration;
            }

            throw new InvalidOperationException($"Failed to retrieve JWKS from {_jwksUrl}", ex);
        }
    }
}
