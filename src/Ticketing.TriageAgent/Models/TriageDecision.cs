namespace Ticketing.TriageAgent.Models;

/// <summary>
/// The LLM's triage decision for a ticket.
/// </summary>
public class TriageDecision
{
    /// <summary>
    /// Target queue: "Helpdesk" or "Purchasing".
    /// </summary>
    public required string Queue { get; set; }

    /// <summary>
    /// Priority level: "Low", "Medium", "High", or "Critical".
    /// </summary>
    public required string Priority { get; set; }

    /// <summary>
    /// Category: "Hardware", "Software", "Network", "Access", or "Other".
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// LLM reasoning for the triage decision.
    /// </summary>
    public required string Reasoning { get; set; }
}
