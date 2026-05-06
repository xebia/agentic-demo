using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Workflow.Executors;

/// <summary>
/// Gets a quote from the FulfillmentAgent for the analyzed items. Escalates if
/// any item is unavailable in the vendor catalog.
/// </summary>
[SendsMessage(typeof(PurchaseContext))]
[SendsMessage(typeof(EscalateRequest))]
public sealed class GetQuoteExecutor : Executor<PurchaseContext>
{
    private readonly FulfillmentApiClient _fulfillmentClient;
    private readonly ILogger<GetQuoteExecutor> _logger;

    public GetQuoteExecutor(
        FulfillmentApiClient fulfillmentClient,
        ILogger<GetQuoteExecutor> logger)
        : base("purchasing.get-quote")
    {
        _fulfillmentClient = fulfillmentClient;
        _logger = logger;
    }

    public override async ValueTask HandleAsync(
        PurchaseContext message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Analysis is null)
        {
            throw new InvalidOperationException("GetQuoteExecutor invoked without an analysis");
        }

        var request = new QuoteRequest
        {
            TicketId = message.TicketId,
            Items = message.Analysis.Items.Select(i => i.Description).ToList()
        };

        var quote = await _fulfillmentClient.GetQuoteAsync(request, cancellationToken);

        _logger.LogInformation(
            "Quote for ticket {TicketId}: {Total:C}, available={Available}",
            message.TicketId, quote.TotalEstimate, quote.Available);

        if (!quote.Available)
        {
            var unavailableItems = quote.LineItems
                .Where(i => !i.Available)
                .Select(i => i.Name)
                .ToList();

            var quoteDetails = string.Join("\n", quote.LineItems.Select(i =>
                $"- {i.Sku}: {i.Name} — ${i.UnitPrice:F2} x {i.Quantity}{(i.Available ? "" : " (UNAVAILABLE)")}"));

            var existingNotes = message.Ticket.TriageNotes ?? "";
            var escalationNotes = $"{existingNotes}\n\n--- Purchasing Analysis ({DateTime.UtcNow:u}) ---\n"
                + $"{message.Analysis.Reasoning}\n\nQuote Details:\n{quoteDetails}\n\n"
                + $"Unavailable items: {string.Join(", ", unavailableItems)}\n"
                + "The requested items are not available from the vendor catalog. "
                + "Human review is needed to find alternatives or cancel the request.";

            await context.SendMessageAsync(
                new EscalateRequest(message.TicketId, escalationNotes),
                cancellationToken: cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            message with { Quote = quote },
            cancellationToken: cancellationToken);
    }
}
