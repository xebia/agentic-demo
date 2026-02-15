using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.OperationsAgent.Models;

namespace Ticketing.OperationsAgent.Services;

/// <summary>
/// Typed HTTP client that POSTs alerts to the Ticketing Web API operations endpoint.
/// </summary>
public class AlertApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthTokenProvider _authTokenProvider;
    private readonly ILogger<AlertApiClient> _logger;

    public AlertApiClient(
        HttpClient httpClient,
        AuthTokenProvider authTokenProvider,
        IConfiguration configuration,
        ILogger<AlertApiClient> logger)
    {
        _httpClient = httpClient;
        _authTokenProvider = authTokenProvider;
        _logger = logger;

        var baseUrl = configuration["TicketingApi:BaseUrl"]
            ?? throw new InvalidOperationException("TicketingApi:BaseUrl is not configured");
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task PostAlertAsync(OperationsAlert alert, CancellationToken cancellationToken = default)
    {
        var token = await _authTokenProvider.GetTokenAsync(cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        _logger.LogInformation("Posting alert: [{Severity}] {Title}", alert.Severity, alert.Title);

        var response = await _httpClient.PostAsJsonAsync(
            "/api/operations/alerts",
            alert,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to post alert: {StatusCode} - {Body}", response.StatusCode, body);
        }
    }
}
