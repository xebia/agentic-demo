using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.PurchasingAgent.Models;

namespace Ticketing.PurchasingAgent.Services;

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

    public async Task<List<TicketListItemResponse>> GetTriagedPurchasingTicketsAsync(CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        _logger.LogInformation("Fetching Triaged+Purchasing tickets from API");

        var response = await _httpClient.GetFromJsonAsync<TicketListResponse>(
            "/api/tickets?status=Triaged&queue=Purchasing&limit=100",
            cancellationToken);

        var tickets = response?.Items ?? [];
        _logger.LogInformation("Found {Count} Triaged+Purchasing tickets", tickets.Count);
        return tickets;
    }

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
