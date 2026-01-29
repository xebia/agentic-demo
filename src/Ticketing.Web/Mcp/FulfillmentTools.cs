using System.ComponentModel;
using Csla;
using ModelContextProtocol.Server;
using Ticketing.Domain;
using Ticketing.Web.Services.Auth;

namespace Ticketing.Web.Mcp;

/// <summary>
/// MCP tools for the fulfillment agent to manage purchase and delivery workflows.
/// These tools are specifically designed for agents handling hardware delivery workflows.
/// </summary>
[McpServerToolType]
public class FulfillmentTools(
    IDataPortal<TicketEdit> ticketEditPortal,
    IDataPortal<TicketList> ticketListPortal,
    McpUserContext userContext)
{

    /// <summary>
    /// Create a delivery ticket as a child of a purchase ticket.
    /// This is used when hardware has arrived and needs to be delivered to the requestor.
    /// </summary>
    [McpServerTool(Name = "fulfillment_create_delivery_ticket")]
    [Description("Create a delivery ticket as a child of an approved/fulfilled purchase ticket. Use this when hardware has arrived and needs to be delivered to the end user.")]
    public async Task<FulfillmentResult> CreateDeliveryTicket(
        [Description("The parent purchase ticket ID (e.g., TKT-00001)")] string parentTicketId,
        [Description("Title for the delivery ticket (e.g., 'Deliver laptop to John Smith')")] string title,
        [Description("Description with delivery details (location, contact info, special instructions)")] string? description = null,
        [Description("Priority: Low, Medium, High, or Critical (default: inherits from parent)")] string? priority = null)
    {
        if (!userContext.IsAuthenticated)
        {
            return new FulfillmentResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (!userContext.HasElevatedAccess)
        {
            return new FulfillmentResult { Error = "Access denied. Creating delivery tickets requires HelpDesk or Approver role." };
        }

        if (string.IsNullOrWhiteSpace(parentTicketId))
        {
            return new FulfillmentResult { Error = "Parent ticket ID is required." };
        }

        // Fetch parent ticket to validate it exists and get context
        TicketEdit parentTicket;
        try
        {
            parentTicket = await ticketEditPortal.FetchAsync(parentTicketId);
        }
        catch (DataNotFoundException)
        {
            return new FulfillmentResult { Error = $"Parent ticket '{parentTicketId}' not found." };
        }

        // Validate parent ticket type and status
        if (parentTicket.TicketTypeValue != TicketType.Purchase)
        {
            return new FulfillmentResult { Error = $"Parent ticket must be a Purchase ticket. Ticket '{parentTicketId}' is a {parentTicket.TicketTypeValue} ticket." };
        }

        var validStatuses = new[] { TicketStatus.Approved, TicketStatus.PendingFulfillment, TicketStatus.Fulfilled };
        if (!validStatuses.Contains(parentTicket.Status))
        {
            return new FulfillmentResult { Error = $"Parent purchase ticket must be in Approved, PendingFulfillment, or Fulfilled status. Current status: {parentTicket.Status}" };
        }

        // Create the delivery ticket
        var deliveryTicket = await ticketEditPortal.CreateAsync(parentTicket.CreatedBy, parentTicket.CreatedByName);
        deliveryTicket.Title = title;
        deliveryTicket.Description = description ?? $"Delivery for {parentTicket.Title}";
        deliveryTicket.TicketTypeValue = TicketType.Delivery;
        deliveryTicket.Category = parentTicket.Category;
        deliveryTicket.ParentTicketId = parentTicketId;
        deliveryTicket.AssignedQueue = TicketQueue.Fulfillment;
        deliveryTicket.Status = TicketStatus.InProgress;

        // Set priority - inherit from parent if not specified
        if (!string.IsNullOrEmpty(priority) && Enum.TryParse<TicketPriority>(priority, ignoreCase: true, out var priorityValue))
        {
            deliveryTicket.Priority = priorityValue;
        }
        else
        {
            deliveryTicket.Priority = parentTicket.Priority;
        }

        // Validate
        if (!deliveryTicket.IsValid)
        {
            var errors = deliveryTicket.BrokenRulesCollection
                .Select(r => $"{r.Property}: {r.Description}")
                .ToList();
            return new FulfillmentResult { Error = $"Validation failed: {string.Join("; ", errors)}" };
        }

        deliveryTicket = await deliveryTicket.SaveAsync();

        return new FulfillmentResult
        {
            Success = true,
            Message = $"Delivery ticket {deliveryTicket.TicketId} created as child of {parentTicketId}.",
            DeliveryTicket = TicketDetail.FromTicketEdit(deliveryTicket),
            ParentTicketId = parentTicketId
        };
    }

    /// <summary>
    /// Mark a delivery as complete and optionally close the parent purchase ticket.
    /// </summary>
    [McpServerTool(Name = "fulfillment_complete_delivery")]
    [Description("Mark a delivery ticket as complete (Fulfilled status). Optionally provide notes about the delivery.")]
    public async Task<FulfillmentResult> CompleteDelivery(
        [Description("The delivery ticket ID to complete (e.g., TKT-00002)")] string deliveryTicketId,
        [Description("Notes about the delivery (who received it, when, etc.)")] string? deliveryNotes = null)
    {
        if (!userContext.IsAuthenticated)
        {
            return new FulfillmentResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (!userContext.HasElevatedAccess)
        {
            return new FulfillmentResult { Error = "Access denied. Completing deliveries requires HelpDesk or Approver role." };
        }

        if (string.IsNullOrWhiteSpace(deliveryTicketId))
        {
            return new FulfillmentResult { Error = "Delivery ticket ID is required." };
        }

        // Fetch delivery ticket
        TicketEdit deliveryTicket;
        try
        {
            deliveryTicket = await ticketEditPortal.FetchAsync(deliveryTicketId);
        }
        catch (DataNotFoundException)
        {
            return new FulfillmentResult { Error = $"Delivery ticket '{deliveryTicketId}' not found." };
        }

        // Validate it's a delivery ticket
        if (deliveryTicket.TicketTypeValue != TicketType.Delivery)
        {
            return new FulfillmentResult { Error = $"Ticket '{deliveryTicketId}' is not a Delivery ticket (type: {deliveryTicket.TicketTypeValue})." };
        }

        // Update the delivery ticket
        deliveryTicket.Status = TicketStatus.Fulfilled;
        if (!string.IsNullOrEmpty(deliveryNotes))
        {
            deliveryTicket.ResolutionNotes = deliveryNotes;
        }

        deliveryTicket = await deliveryTicket.SaveAsync();

        return new FulfillmentResult
        {
            Success = true,
            Message = $"Delivery ticket {deliveryTicketId} marked as fulfilled.",
            DeliveryTicket = TicketDetail.FromTicketEdit(deliveryTicket),
            ParentTicketId = deliveryTicket.ParentTicketId
        };
    }

    /// <summary>
    /// Get the purchase workflow status for a ticket, including parent and all child delivery tickets.
    /// </summary>
    [McpServerTool(Name = "fulfillment_get_workflow_status")]
    [Description("Get the complete status of a purchase workflow, including the parent purchase ticket and all related delivery tickets.")]
    public async Task<WorkflowStatusResult> GetWorkflowStatus(
        [Description("The purchase or delivery ticket ID to get workflow status for")] string ticketId)
    {
        if (!userContext.IsAuthenticated)
        {
            return new WorkflowStatusResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return new WorkflowStatusResult { Error = "Ticket ID is required." };
        }

        // Fetch the ticket
        TicketEdit ticket;
        try
        {
            ticket = await ticketEditPortal.FetchAsync(ticketId);
        }
        catch (DataNotFoundException)
        {
            return new WorkflowStatusResult { Error = $"Ticket '{ticketId}' not found." };
        }

        // Authorization
        if (!userContext.HasElevatedAccess && ticket.CreatedBy != userContext.CurrentUserId)
        {
            return new WorkflowStatusResult { Error = $"Access denied. You don't have permission to view ticket {ticketId}." };
        }

        // Determine the parent ticket ID
        string parentTicketId;
        TicketDetail parentTicket;

        if (ticket.TicketTypeValue == TicketType.Purchase)
        {
            // This is the parent
            parentTicketId = ticket.TicketId;
            parentTicket = TicketDetail.FromTicketEdit(ticket);
        }
        else if (ticket.TicketTypeValue == TicketType.Delivery && !string.IsNullOrEmpty(ticket.ParentTicketId))
        {
            // This is a child delivery ticket, fetch the parent
            parentTicketId = ticket.ParentTicketId;
            try
            {
                var parent = await ticketEditPortal.FetchAsync(parentTicketId);
                parentTicket = TicketDetail.FromTicketEdit(parent);
            }
            catch (DataNotFoundException)
            {
                return new WorkflowStatusResult 
                { 
                    Error = $"Parent ticket '{parentTicketId}' not found.",
                    DeliveryTicket = TicketDetail.FromTicketEdit(ticket)
                };
            }
        }
        else
        {
            // Not a purchase workflow
            return new WorkflowStatusResult
            {
                Error = $"Ticket '{ticketId}' is not part of a purchase workflow. Type: {ticket.TicketTypeValue}",
                SingleTicket = TicketDetail.FromTicketEdit(ticket)
            };
        }

        // Fetch all child delivery tickets
        var childCriteria = new TicketListCriteria
        {
            ParentTicketId = parentTicketId,
            IncludeChildren = true,
            Limit = 100
        };

        var children = await ticketListPortal.FetchAsync(childCriteria);
        var childTickets = children.Select(c => TicketSummary.FromTicketInfo(c)).ToList();

        // Calculate workflow status
        var allChildrenCompleted = childTickets.Count > 0 && 
            childTickets.All(c => c.Status == TicketStatus.Fulfilled.ToString() || 
                                  c.Status == TicketStatus.Closed.ToString());

        var workflowStatus = "Unknown";
        if (parentTicket.Status == TicketStatus.Closed.ToString())
        {
            workflowStatus = "Complete";
        }
        else if (allChildrenCompleted && childTickets.Count > 0)
        {
            workflowStatus = "ReadyToClose";
        }
        else if (childTickets.Count > 0)
        {
            workflowStatus = "InProgress";
        }
        else if (parentTicket.Status == TicketStatus.Approved.ToString() || 
                 parentTicket.Status == TicketStatus.PendingFulfillment.ToString())
        {
            workflowStatus = "PendingDelivery";
        }
        else if (parentTicket.Status == TicketStatus.PendingApproval.ToString())
        {
            workflowStatus = "PendingApproval";
        }
        else
        {
            workflowStatus = parentTicket.Status;
        }

        return new WorkflowStatusResult
        {
            PurchaseTicket = parentTicket,
            DeliveryTickets = childTickets,
            WorkflowStatus = workflowStatus,
            AllDeliveriesComplete = allChildrenCompleted,
            DeliveryCount = childTickets.Count,
            Message = workflowStatus == "ReadyToClose" 
                ? $"All {childTickets.Count} delivery ticket(s) are complete. Parent purchase ticket {parentTicketId} can be closed."
                : null
        };
    }

    /// <summary>
    /// Update a purchase ticket to pending fulfillment status after approval.
    /// </summary>
    [McpServerTool(Name = "fulfillment_mark_pending")]
    [Description("Update an approved purchase ticket to PendingFulfillment status. Use this when hardware is ordered and awaiting delivery.")]
    public async Task<FulfillmentResult> MarkPendingFulfillment(
        [Description("The purchase ticket ID (e.g., TKT-00001)")] string ticketId,
        [Description("Notes about the order (PO number, expected delivery date, vendor, etc.)")] string? orderNotes = null)
    {
        if (!userContext.IsAuthenticated)
        {
            return new FulfillmentResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (!userContext.HasElevatedAccess)
        {
            return new FulfillmentResult { Error = "Access denied. Updating purchase status requires HelpDesk or Approver role." };
        }

        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return new FulfillmentResult { Error = "Ticket ID is required." };
        }

        // Fetch ticket
        TicketEdit ticket;
        try
        {
            ticket = await ticketEditPortal.FetchAsync(ticketId);
        }
        catch (DataNotFoundException)
        {
            return new FulfillmentResult { Error = $"Ticket '{ticketId}' not found." };
        }

        // Validate ticket type
        if (ticket.TicketTypeValue != TicketType.Purchase)
        {
            return new FulfillmentResult { Error = $"Ticket '{ticketId}' is not a Purchase ticket (type: {ticket.TicketTypeValue})." };
        }

        // Validate status
        if (ticket.Status != TicketStatus.Approved)
        {
            return new FulfillmentResult { Error = $"Ticket must be in Approved status. Current status: {ticket.Status}" };
        }

        // Update
        ticket.Status = TicketStatus.PendingFulfillment;
        ticket.AssignedQueue = TicketQueue.Fulfillment;
        if (!string.IsNullOrEmpty(orderNotes))
        {
            ticket.TriageNotes = (ticket.TriageNotes ?? "") + $"\n[Order Info]: {orderNotes}";
        }

        ticket = await ticket.SaveAsync();

        return new FulfillmentResult
        {
            Success = true,
            Message = $"Purchase ticket {ticketId} marked as PendingFulfillment.",
            PurchaseTicket = TicketDetail.FromTicketEdit(ticket)
        };
    }
}

#region Fulfillment Result Models

public class FulfillmentResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public TicketDetail? DeliveryTicket { get; set; }
    public TicketDetail? PurchaseTicket { get; set; }
    public string? ParentTicketId { get; set; }
}

public class WorkflowStatusResult
{
    public string? Error { get; set; }
    public string? Message { get; set; }
    public TicketDetail? PurchaseTicket { get; set; }
    public TicketDetail? DeliveryTicket { get; set; }
    public TicketDetail? SingleTicket { get; set; }
    public List<TicketSummary> DeliveryTickets { get; set; } = [];
    public string? WorkflowStatus { get; set; }
    public bool AllDeliveriesComplete { get; set; }
    public int DeliveryCount { get; set; }
}

#endregion
