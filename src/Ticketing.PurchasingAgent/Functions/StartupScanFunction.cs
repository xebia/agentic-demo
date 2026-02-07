using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Functions;

public class StartupScanFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly PurchasingFunction _purchasingFunction;
    private readonly ILogger<StartupScanFunction> _logger;

    public StartupScanFunction(
        TicketingApiClient apiClient,
        PurchasingFunction purchasingFunction,
        ILogger<StartupScanFunction> logger)
    {
        _apiClient = apiClient;
        _purchasingFunction = purchasingFunction;
        _logger = logger;
    }

    [Function("PurchasingStartupScan")]
    public async Task Run(
        [TimerTrigger("0 0 */12 * * *", RunOnStartup = true)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting scan for Triaged+Purchasing tickets");

        var tickets = await _apiClient.GetTriagedPurchasingTicketsAsync(cancellationToken);
        if (tickets.Count == 0)
        {
            _logger.LogInformation("No Triaged+Purchasing tickets found");
            return;
        }

        _logger.LogInformation("Found {Count} Triaged+Purchasing tickets", tickets.Count);

        foreach (var ticket in tickets)
        {
            try
            {
                await _purchasingFunction.ProcessPurchaseTicketByIdAsync(ticket.TicketId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process ticket {TicketId}", ticket.TicketId);
            }
        }

        _logger.LogInformation("Purchasing startup scan complete");
    }
}
