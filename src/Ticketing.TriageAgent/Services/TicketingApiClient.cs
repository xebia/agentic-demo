using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.TriageAgent.Models;

namespace Ticketing.TriageAgent.Services;

/// <summary>
/// Typed HTTP client for the Ticketing REST API.
/// Injects Bearer token from AuthTokenProvider on each request.
/// </summary>
public class TicketingApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthTokenProvider _authTokenProvider;
    private readonly ILogger<TicketingApiClient> _logger;

    public TicketingApiClient(
        HttpClient httpClient,
        AuthTokenProvider authTokenProvider,
        IConfiguration configuration,
        ILogger<TicketingApiClient> logger)
    {
        _httpClient = httpClient;
        _authTokenProvider = authTokenProvider;
        _logger = logger;

        var baseUrl = configuration["TicketingApi:BaseUrl"]
            ?? throw new InvalidOperationException("TicketingApi:BaseUrl is not configured");
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    private async Task SetAuthHeaderAsync(CancellationToken cancellationToken)
    {
        var token = await _authTokenProvider.GetTokenAsync(cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Gets all tickets with status "New".
    /// </summary>
    public async Task<List<TicketListItemResponse>> GetNewTicketsAsync(CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        _logger.LogInformation("Fetching new tickets from API");

        var response = await _httpClient.GetFromJsonAsync<TicketListResponse>(
            "/api/tickets?status=New&limit=100",
            cancellationToken);

        var tickets = response?.Items ?? [];
        _logger.LogInformation("Found {Count} new tickets", tickets.Count);
        return tickets;
    }

    /// <summary>
    /// Gets a single ticket by ID.
    /// </summary>
    public async Task<TicketDetailResponse?> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        _logger.LogInformation("Fetching ticket {TicketId}", ticketId);

        var response = await _httpClient.GetAsync($"/api/tickets/{ticketId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch ticket {TicketId}: {StatusCode}", ticketId, response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TicketDetailResponse>(cancellationToken);
    }

    /// <summary>
    /// Updates a ticket via the REST API.
    /// </summary>
    public async Task<TicketDetailResponse?> UpdateTicketAsync(
        string ticketId,
        UpdateTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        _logger.LogInformation("Updating ticket {TicketId}", ticketId);

        var response = await _httpClient.PutAsJsonAsync(
            $"/api/tickets/{ticketId}",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Failed to update ticket {TicketId}: {StatusCode} - {Body}",
                ticketId, response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TicketDetailResponse>(cancellationToken);
    }
}
