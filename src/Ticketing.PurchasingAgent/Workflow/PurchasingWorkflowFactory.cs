using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.PurchasingAgent.Workflow.Executors;

namespace Ticketing.PurchasingAgent.Workflow;

/// <summary>
/// Builds a fresh purchasing workflow per invocation. Executors are resolved
/// from the per-invocation service scope so each Run gets its own DI-scoped
/// dependencies (typed HttpClients, scoped services).
/// </summary>
public sealed class PurchasingWorkflowFactory
{
    private readonly IServiceProvider _services;
    public static readonly RequestPort ApprovalPort =
        RequestPort.Create<ApprovalRequest, ApprovalResponse>(WorkflowIds.ApprovalPort);

    public PurchasingWorkflowFactory(IServiceProvider services)
    {
        _services = services;
    }

    public Microsoft.Agents.AI.Workflows.Workflow Build()
    {
        var fetch = _services.GetRequiredService<FetchTicketExecutor>();
        var analyze = _services.GetRequiredService<AnalyzeRequestExecutor>();
        var quote = _services.GetRequiredService<GetQuoteExecutor>();
        var decide = _services.GetRequiredService<DecideExecutor>();
        var bridge = _services.GetRequiredService<ApprovalBridgeExecutor>();
        var apply = _services.GetRequiredService<ApplyApprovalExecutor>();
        var escalate = _services.GetRequiredService<EscalateExecutor>();

        // Type-discriminated routing: each AddEdge<T>(... condition: m => m is not null)
        // ensures the edge only fires for messages of type T (non-T messages cast to null
        // and the predicate returns false). Multiple typed edges from the same source
        // implement the discriminated-union routing this workflow needs.
        return new WorkflowBuilder(fetch)
            .WithName("PurchasingApprovalWorkflow")
            .WithDescription("Analyze → quote → decide → (auto-approve | human-approve) → fulfillment-or-rejection")
            .AddEdge<PurchaseContext>(fetch, analyze, m => m is not null)
            .AddEdge<EscalateRequest>(fetch, escalate, m => m is not null)
            .AddEdge<PurchaseContext>(analyze, quote, m => m is not null)
            .AddEdge<EscalateRequest>(analyze, escalate, m => m is not null)
            .AddEdge<PurchaseContext>(quote, decide, m => m is not null)
            .AddEdge<EscalateRequest>(quote, escalate, m => m is not null)
            .AddEdge<ApprovalRequest>(decide, ApprovalPort, m => m is not null)
            .AddEdge<ApprovalResolved>(decide, apply, m => m is not null)
            .AddEdge(ApprovalPort, bridge)
            .AddEdge<ApprovalResolved>(bridge, apply, m => m is not null)
            .WithOutputFrom(apply, escalate, fetch)
            .Build();
    }
}
