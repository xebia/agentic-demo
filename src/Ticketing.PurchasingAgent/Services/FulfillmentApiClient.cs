using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.PurchasingAgent.Models;

namespace Ticketing.PurchasingAgent.Services;

public class FulfillmentApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FulfillmentApiClient> _logger;

    public FulfillmentApiClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FulfillmentApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var baseUrl = configuration["FulfillmentApi:BaseUrl"]
            ?? throw new InvalidOperationException("FulfillmentApi:BaseUrl is not configured");
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<QuoteResponse> GetQuoteAsync(QuoteRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Requesting quote for ticket {TicketId} with {ItemCount} items",
            request.TicketId, request.Items.Count);

        var response = await _httpClient.PostAsJsonAsync("/api/fulfillment/quote", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<QuoteResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize quote response");
    }
}
