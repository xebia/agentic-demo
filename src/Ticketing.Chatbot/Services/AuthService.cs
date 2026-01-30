using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Ticketing.Chatbot.Models;

namespace Ticketing.Chatbot.Services;

/// <summary>
/// Service for interacting with the auth service.
/// </summary>
public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly ChatSettings _settings;

    public AuthService(HttpClient httpClient, IOptions<ChatSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.BaseAddress = new Uri(_settings.AuthServiceUrl);
    }

    /// <summary>
    /// Gets the list of available demo users from the auth service.
    /// </summary>
    public async Task<List<DemoUser>> GetAvailableUsersAsync()
    {
        var response = await _httpClient.GetAsync("/users");
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<List<DemoUser>>();
        return users ?? [];
    }

    /// <summary>
    /// Gets a JWT token for the specified user email.
    /// </summary>
    public async Task<TokenResponse?> GetTokenAsync(string email)
    {
        var request = new { email };
        var response = await _httpClient.PostAsJsonAsync("/token", request);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>();
    }
}
