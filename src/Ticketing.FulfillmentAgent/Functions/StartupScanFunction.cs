using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.FulfillmentAgent.Services;

namespace Ticketing.FulfillmentAgent.Functions;

public class StartupScanFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly FulfillmentFunction _fulfillmentFunction;
    private readonly ILogger<StartupScanFunction> _logger;

    public StartupScanFunction(
        TicketingApiClient apiClient,
        FulfillmentFunction fulfillmentFunction,
        ILogger<StartupScanFunction> logger)
    {
        _apiClient = apiClient;
        _fulfillmentFunction = fulfillmentFunction;
        _logger = logger;
    }

    [Function("FulfillmentStartupScan")]
    public async Task Run(
        [TimerTrigger("0 0 */12 * * *", RunOnStartup = true)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        // Add jitter to prevent thundering herd when all agents scan simultaneously
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(0, 60)), cancellationToken);

        _logger.LogInformation("Starting scan for PendingFulfillment tickets");

        var tickets = await _apiClient.GetPendingFulfillmentTicketsAsync(cancellationToken);
        if (tickets.Count == 0)
        {
            _logger.LogInformation("No PendingFulfillment tickets found");
            return;
        }

        _logger.LogInformation("Found {Count} PendingFulfillment tickets", tickets.Count);

        foreach (var ticket in tickets)
        {
            try
            {
                await _fulfillmentFunction.FulfillTicketByIdAsync(ticket.TicketId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process ticket {TicketId}", ticket.TicketId);
            }
        }

        _logger.LogInformation("Fulfillment startup scan complete");
    }
}
