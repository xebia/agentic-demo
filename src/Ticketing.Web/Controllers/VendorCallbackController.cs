using Csla;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Domain;
using Ticketing.Messaging.Abstractions;
using Ticketing.Web.Controllers.Models;

namespace Ticketing.Web.Controllers;

/// <summary>
/// Receives vendor delivery/fulfillment callbacks from the Vendor Mock service.
/// No JWT auth required — this endpoint is called by the vendor mock.
/// </summary>
[ApiController]
[Route("api/vendor")]
public class VendorCallbackController : ControllerBase
{
    private readonly IDataPortal<TicketEdit> _ticketEditPortal;
    private readonly IDataPortal<TicketList> _ticketListPortal;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<VendorCallbackController> _logger;

    public VendorCallbackController(
        IDataPortal<TicketEdit> ticketEditPortal,
        IDataPortal<TicketList> ticketListPortal,
        IEventPublisher eventPublisher,
        ILogger<VendorCallbackController> logger)
    {
        _ticketEditPortal = ticketEditPortal;
        _ticketListPortal = ticketListPortal;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    [HttpPost("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HandleVendorCallback([FromBody] VendorCallbackRequest request)
    {
        _logger.LogInformation(
            "Vendor callback received: OrderId={OrderId}, TicketId={TicketId}, Status={Status}",
            request.OrderId, request.TicketId, request.Status);

        TicketEdit ticket;
        try
        {
            ticket = await _ticketEditPortal.FetchAsync(request.TicketId);
        }
        catch (DataNotFoundException)
        {
            _logger.LogWarning("Ticket {TicketId} not found for vendor callback", request.TicketId);
            return NotFound();
        }

        if (string.Equals(request.Status, "delivered", StringComparison.OrdinalIgnoreCase))
        {
            // Update purchase ticket to Fulfilled
            ticket.Status = TicketStatus.Fulfilled;
            ticket.ResolutionNotes = $"Order {request.OrderId} delivered. {request.Message}";

            var deliveryItemSummary = string.Join("\n", request.Items.Select(i => $"  - {i.Name} (SKU: {i.Sku}) x{i.Quantity}"));
            var existingTriageNotes = ticket.TriageNotes ?? "";
            ticket.TriageNotes = $"{existingTriageNotes}\n\n--- Delivery Completed ({DateTime.UtcNow:u}) ---\nOrder ID: {request.OrderId}\nItems delivered:\n{deliveryItemSummary}\n{request.Message}";

            ticket = await ticket.SaveAsync();

            _logger.LogInformation("Ticket {TicketId} marked as Fulfilled", request.TicketId);

            // Create delivery child ticket for helpdesk
            var itemSummary = string.Join(", ", request.Items.Select(i => $"{i.Name} x{i.Quantity}"));
            var deliveryTicket = await _ticketEditPortal.CreateAsync("system", "System");
            deliveryTicket.Title = $"Deliver {itemSummary} to {ticket.CreatedByName ?? ticket.CreatedBy}";
            deliveryTicket.Description = $"Hardware delivery for purchase ticket {request.TicketId}.\n\nItems:\n{string.Join("\n", request.Items.Select(i => $"- {i.Name} (SKU: {i.Sku}) x{i.Quantity}"))}\n\nOrder ID: {request.OrderId}";
            deliveryTicket.TicketTypeValue = TicketType.Delivery;
            deliveryTicket.Category = TicketCategory.Hardware;
            deliveryTicket.Priority = ticket.Priority;
            deliveryTicket.Status = TicketStatus.New;
            deliveryTicket.AssignedQueue = TicketQueue.Helpdesk;
            deliveryTicket.ParentTicketId = request.TicketId;
            deliveryTicket = await deliveryTicket.SaveAsync();

            _logger.LogInformation(
                "Created delivery ticket {DeliveryTicketId} as child of {ParentTicketId}",
                deliveryTicket.TicketId, request.TicketId);

            // Publish fulfilled event
            await _eventPublisher.PublishAsync(new TicketEvent
            {
                EventType = TicketEventTypes.TicketFulfilled,
                Payload = new TicketEventPayload
                {
                    TicketId = request.TicketId,
                    Title = ticket.Title,
                    Status = "Fulfilled",
                    ChangedBy = "vendor-callback"
                }
            });
        }
        else if (string.Equals(request.Status, "unfulfillable", StringComparison.OrdinalIgnoreCase))
        {
            // Reassign to Purchasing for human review instead of dead-ending as Rejected
            ticket.Status = TicketStatus.InProgress;
            ticket.AssignedQueue = TicketQueue.Purchasing;
            ticket.ResolutionNotes = $"Order {request.OrderId} could not be fulfilled: {request.Message}";

            var existingTriageNotes = ticket.TriageNotes ?? "";
            ticket.TriageNotes = $"{existingTriageNotes}\n\n--- Vendor Fulfillment Failed ({DateTime.UtcNow:u}) ---\nOrder ID: {request.OrderId}\nReason: {request.Message}\nReassigned to Purchasing queue for human review and alternative sourcing.";

            ticket = await ticket.SaveAsync();

            _logger.LogInformation(
                "Ticket {TicketId} reassigned to Purchasing (vendor unfulfillable)", request.TicketId);

            await _eventPublisher.PublishAsync(new TicketEvent
            {
                EventType = TicketEventTypes.TicketAssigned,
                Payload = new TicketEventPayload
                {
                    TicketId = request.TicketId,
                    Title = ticket.Title,
                    Status = "InProgress",
                    AssignedQueue = "Purchasing",
                    ChangedBy = "vendor-callback"
                }
            });
        }

        return Ok();
    }
}

/// <summary>
/// Vendor callback request payload.
/// </summary>
public class VendorCallbackRequest
{
    public required string OrderId { get; set; }
    public required string TicketId { get; set; }
    public required string Status { get; set; }
    public required string Message { get; set; }
    public required List<VendorCallbackItem> Items { get; set; }
}

public class VendorCallbackItem
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public int Quantity { get; set; }
}
