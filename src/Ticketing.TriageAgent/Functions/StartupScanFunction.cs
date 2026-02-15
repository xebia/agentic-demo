using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.TriageAgent.Services;

namespace Ticketing.TriageAgent.Functions;

/// <summary>
/// Timer trigger that scans for untriaged tickets on startup and every 12 hours.
/// Catches any tickets that were created while the agent was offline.
/// </summary>
public class StartupScanFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly TriageFunction _triageFunction;
    private readonly ILogger<StartupScanFunction> _logger;

    public StartupScanFunction(
        TicketingApiClient apiClient,
        TriageFunction triageFunction,
        ILogger<StartupScanFunction> logger)
    {
        _apiClient = apiClient;
        _triageFunction = triageFunction;
        _logger = logger;
    }

    [Function("StartupScan")]
    public async Task Run(
        [TimerTrigger("0 0 */12 * * *", RunOnStartup = true)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        // Add jitter to prevent thundering herd when all agents scan simultaneously
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(0, 60)), cancellationToken);

        _logger.LogInformation("Starting scan for untriaged tickets");

        var newTickets = await _apiClient.GetNewTicketsAsync(cancellationToken);
        if (newTickets.Count == 0)
        {
            _logger.LogInformation("No new tickets found");
            return;
        }

        _logger.LogInformation("Found {Count} new tickets to triage", newTickets.Count);

        foreach (var ticket in newTickets)
        {
            try
            {
                await _triageFunction.TriageTicketByIdAsync(ticket.TicketId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to triage ticket {TicketId}", ticket.TicketId);
            }
        }

        _logger.LogInformation("Startup scan complete");
    }
}
