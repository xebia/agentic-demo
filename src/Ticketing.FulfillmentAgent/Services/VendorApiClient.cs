using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.FulfillmentAgent.Models;

namespace Ticketing.FulfillmentAgent.Services;

public class VendorApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VendorApiClient> _logger;

    public VendorApiClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<VendorApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var baseUrl = configuration["VendorApi:BaseUrl"]
            ?? throw new InvalidOperationException("VendorApi:BaseUrl is not configured");
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<List<VendorProduct>> SearchCatalogAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching vendor catalog for: {Query}", query);

        var products = await _httpClient.GetFromJsonAsync<List<VendorProduct>>(
            $"/api/catalog/search?query={Uri.EscapeDataString(query)}",
            cancellationToken);

        return products ?? [];
    }

    public async Task<VendorOrderResponse> SubmitOrderAsync(VendorOrderRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Submitting order to vendor for ticket {TicketId}", request.TicketId);

        var response = await _httpClient.PostAsJsonAsync("/api/orders", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<VendorOrderResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize vendor order response");
    }
}
