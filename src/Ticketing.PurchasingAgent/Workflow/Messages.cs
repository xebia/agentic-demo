using Ticketing.PurchasingAgent.Models;

namespace Ticketing.PurchasingAgent.Workflow;

/// <summary>
/// Workflow input — kicks off purchasing for a single ticket.
/// </summary>
public record PurchaseStart(string TicketId);

/// <summary>
/// Threaded through the analyze → quote → decide chain. Later steps add fields
/// (Analysis, Quote) and pass the enriched context downstream.
/// </summary>
public record PurchaseContext(
    string TicketId,
    TicketDetailResponse Ticket,
    PurchasingDecision? Analysis,
    QuoteResponse? Quote);

/// <summary>
/// Sent through the human-approval RequestPort. Carries everything the approver
/// UI needs to surface the request — ticket id, title, total, reasoning, line items.
/// </summary>
public record ApprovalRequest(
    string TicketId,
    string Title,
    decimal Total,
    string AiReasoning,
    IReadOnlyList<QuoteLineItem> LineItems);

/// <summary>
/// Response returned from the human approver via the RequestPort.
/// </summary>
public record ApprovalResponse(bool Approved, string? Reason, string? ApproverName);

/// <summary>
/// Final approval decision plus the context needed to apply it. Used by both the
/// auto-approve path (DecideExecutor → Apply directly) and the human path
/// (RequestPort → ApprovalBridge → Apply).
/// </summary>
public record ApprovalResolved(
    PurchaseContext Context,
    bool Approved,
    string Reason,
    string? ApproverName);

/// <summary>
/// Human review needed (no items identified, items unavailable, repeated fulfillment failures).
/// </summary>
public record EscalateRequest(string TicketId, string Notes);

/// <summary>
/// Idempotency / status guard fell through — workflow should halt cleanly without acting.
/// </summary>
public record SkipResult(string TicketId, string Reason);

/// <summary>
/// Constants used to identify ports and label edges for visualization.
/// </summary>
public static class WorkflowIds
{
    public const string ApprovalPort = "purchasing.approval-port";
}
