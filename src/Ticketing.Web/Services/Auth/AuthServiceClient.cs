using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Ticketing.Web.Services.Auth;

/// <summary>
/// Client for interacting with the central auth service.
/// </summary>
public class AuthServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly JwtSettings _settings;
    private readonly ILogger<AuthServiceClient> _logger;

    // Cache users to avoid repeated calls
    private List<MockUser>? _cachedUsers;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public AuthServiceClient(
        HttpClient httpClient,
        IOptions<JwtSettings> settings,
        ILogger<AuthServiceClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.AuthServiceUrl);
    }

    /// <summary>
    /// Gets the list of available users from the auth service.
    /// </summary>
    public async Task<IReadOnlyList<MockUser>> GetAvailableUsersAsync()
    {
        // Return cached users if still valid
        if (_cachedUsers != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedUsers;
        }

        try
        {
            var response = await _httpClient.GetAsync("/users");
            response.EnsureSuccessStatusCode();

            var users = await response.Content.ReadFromJsonAsync<List<AuthServiceUser>>();
            if (users != null)
            {
                _cachedUsers = users.Select(u => new MockUser
                {
                    Id = u.Id,
                    Email = u.Email,
                    DisplayName = u.DisplayName,
                    Roles = u.Roles
                }).ToList();
                _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
                return _cachedUsers;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch users from auth service, falling back to local users");
        }

        // Fallback to local users if auth service is unavailable
        return MockUser.DemoUsers.All;
    }

    /// <summary>
    /// Gets a specific user by ID from the auth service.
    /// </summary>
    public async Task<MockUser?> GetUserByIdAsync(string userId)
    {
        var users = await GetAvailableUsersAsync();
        return users.FirstOrDefault(u => u.Id.Equals(userId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Clears the user cache, forcing a refresh on next request.
    /// </summary>
    public void ClearCache()
    {
        _cachedUsers = null;
        _cacheExpiry = DateTime.MinValue;
    }

    /// <summary>
    /// Authenticates a user by calling the auth service token endpoint.
    /// Returns the token response with user information and JWT token.
    /// </summary>
    /// <param name="userId">The user ID to authenticate</param>
    /// <param name="email">The user email (optional, used as fallback)</param>
    /// <returns>Token response if successful, null if authentication failed</returns>
    public async Task<AuthTokenResponse?> AuthenticateUserAsync(string? userId = null, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("AuthenticateUserAsync called without userId or email");
            return null;
        }

        try
        {
            var request = new { userId, email };
            var response = await _httpClient.PostAsJsonAsync("/token", request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Authentication failed for user {UserId}/{Email}: {StatusCode} - {Error}",
                    userId ?? "(not provided)",
                    email ?? "(not provided)",
                    response.StatusCode,
                    error);
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();

            _logger.LogInformation("User authenticated successfully: {UserId} ({DisplayName})",
                tokenResponse?.Subject?.Id ?? userId,
                tokenResponse?.Subject?.DisplayName ?? "Unknown");

            return tokenResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating user {UserId}/{Email}",
                userId ?? "(not provided)",
                email ?? "(not provided)");
            return null;
        }
    }

    private class AuthServiceUser
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string[] Roles { get; set; } = [];
    }
}
