using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.OperationsAgent.Models;

namespace Ticketing.OperationsAgent.Services;

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

    public async Task<List<TicketListItemResponse>> GetTicketsByStatusAsync(
        string status, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        _logger.LogInformation("Fetching tickets with status {Status}", status);

        var response = await _httpClient.GetFromJsonAsync<TicketListResponse>(
            $"/api/tickets?status={status}&limit=100",
            cancellationToken);

        var tickets = response?.Items ?? [];
        _logger.LogInformation("Found {Count} tickets with status {Status}", tickets.Count, status);
        return tickets;
    }

    public async Task<List<TicketListItemResponse>> GetNewTicketsAsync(
        CancellationToken cancellationToken = default)
        => await GetTicketsByStatusAsync("New", cancellationToken);

    public async Task<List<TicketListItemResponse>> GetTriagedTicketsAsync(
        CancellationToken cancellationToken = default)
        => await GetTicketsByStatusAsync("Triaged", cancellationToken);

    public async Task<List<TicketListItemResponse>> GetPendingApprovalTicketsAsync(
        CancellationToken cancellationToken = default)
        => await GetTicketsByStatusAsync("PendingApproval", cancellationToken);

    public async Task<List<TicketListItemResponse>> GetPendingFulfillmentTicketsAsync(
        CancellationToken cancellationToken = default)
        => await GetTicketsByStatusAsync("PendingFulfillment", cancellationToken);
}
