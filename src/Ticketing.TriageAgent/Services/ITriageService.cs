using Ticketing.TriageAgent.Models;

namespace Ticketing.TriageAgent.Services;

/// <summary>
/// Analyzes a ticket and returns a triage decision.
/// </summary>
public interface ITriageService
{
    Task<TriageDecision> TriageTicketAsync(TicketDetailResponse ticket, CancellationToken cancellationToken = default);
}
