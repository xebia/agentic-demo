using Microsoft.Agents.AI.Workflows;

namespace Ticketing.PurchasingAgent.Workflow.Executors;

/// <summary>
/// Receives the ApprovalResponse emitted by the RequestPort, recovers the original
/// PurchaseContext from shared state (stashed there by DecideExecutor before
/// suspension), and produces the unified ApprovalResolved message that
/// ApplyApprovalExecutor consumes.
/// </summary>
[SendsMessage(typeof(ApprovalResolved))]
public sealed class ApprovalBridgeExecutor : Executor<ApprovalResponse>
{
    public ApprovalBridgeExecutor() : base("purchasing.approval-bridge")
    {
    }

    public override async ValueTask HandleAsync(
        ApprovalResponse message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var ctx = await context.ReadStateAsync<PurchaseContext>(
            DecideExecutor.ContextStateKey,
            DecideExecutor.ContextStateScope,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Approval bridge could not recover purchase context from workflow state");

        var reason = message.Reason ?? (message.Approved ? "Approved by manager" : "Rejected by manager");
        await context.SendMessageAsync(
            new ApprovalResolved(ctx, message.Approved, reason, message.ApproverName),
            cancellationToken: cancellationToken);
    }
}
