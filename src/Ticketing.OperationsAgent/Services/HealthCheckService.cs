using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.OperationsAgent.Models;

namespace Ticketing.OperationsAgent.Services;

/// <summary>
/// Probes /health endpoints on all services and reports their status.
/// </summary>
public class HealthCheckService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly List<(string Name, string Url)> _services;

    public HealthCheckService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HealthCheckService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _logger = logger;

        _services =
        [
            ("AuthService", configuration["AuthService:Url"] ?? ""),
            ("TicketingApi", configuration["TicketingApi:BaseUrl"] ?? ""),
            ("VendorApi", configuration["VendorApi:BaseUrl"] ?? "")
        ];
    }

    public async Task<List<ServiceHealthStatus>> CheckServicesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ServiceHealthStatus>();

        foreach (var (name, url) in _services)
        {
            if (string.IsNullOrEmpty(url))
            {
                results.Add(new ServiceHealthStatus
                {
                    ServiceName = name,
                    Endpoint = "(not configured)",
                    IsHealthy = false,
                    Error = "URL not configured"
                });
                continue;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await _httpClient.GetAsync($"{url}/health", cancellationToken);
                sw.Stop();

                results.Add(new ServiceHealthStatus
                {
                    ServiceName = name,
                    Endpoint = $"{url}/health",
                    IsHealthy = response.IsSuccessStatusCode,
                    Status = response.StatusCode.ToString(),
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "Health check failed for {Service} at {Url}", name, url);
                results.Add(new ServiceHealthStatus
                {
                    ServiceName = name,
                    Endpoint = $"{url}/health",
                    IsHealthy = false,
                    Error = ex.Message,
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds
                });
            }
        }

        return results;
    }
}
