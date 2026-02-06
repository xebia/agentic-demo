using System.ComponentModel;
using Csla;
using ModelContextProtocol.Server;
using Ticketing.Domain;
using Ticketing.Web.Services.Auth;

namespace Ticketing.Web.Mcp;

/// <summary>
/// MCP tools for ticket operations.
/// All operations are performed on behalf of the authenticated user from the JWT token.
/// </summary>
[McpServerToolType]
public class TicketingTools(
    IDataPortal<TicketList> ticketListPortal,
    IDataPortal<TicketEdit> ticketEditPortal,
    McpUserContext userContext)
{

    /// <summary>
    /// List tickets with optional filtering. Regular users see only their own tickets.
    /// HelpDesk and Approver roles can see all tickets.
    /// </summary>
    /// <param name="status">Filter by status (comma-separated): New, Triaged, InProgress, PendingApproval, Approved, Rejected, PendingFulfillment, Fulfilled, Resolved, Closed</param>
    /// <param name="queue">Filter by queue: Helpdesk, Purchasing, Fulfillment</param>
    /// <param name="assignedTo">Filter by assigned user email</param>
    /// <param name="ticketType">Filter by type: Support, Purchase, Delivery</param>
    /// <param name="parentTicketId">Filter by parent ticket ID to get child tickets</param>
    /// <param name="limit">Maximum results (default 50, max 100)</param>
    /// <param name="offset">Number of results to skip for pagination</param>
    /// <returns>List of tickets matching the criteria</returns>
    [McpServerTool(Name = "ticket_list")]
    [Description("List tickets with optional filtering. Returns tickets the authenticated user can access.")]
    public async Task<TicketListResult> ListTickets(
        [Description("Filter by status (comma-separated): New, Triaged, InProgress, PendingApproval, Approved, Rejected, PendingFulfillment, Fulfilled, Resolved, Closed")] string? status = null,
        [Description("Filter by queue: Helpdesk, Purchasing, Fulfillment")] string? queue = null,
        [Description("Filter by assigned user email")] string? assignedTo = null,
        [Description("Filter by type: Support, Purchase, Delivery")] string? ticketType = null,
        [Description("Filter by parent ticket ID to get child tickets")] string? parentTicketId = null,
        [Description("Maximum results (default 50, max 100)")] int limit = 50,
        [Description("Number of results to skip for pagination")] int offset = 0)
    {
        if (!userContext.IsAuthenticated)
        {
            return new TicketListResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        var criteria = new TicketListCriteria
        {
            Limit = Math.Min(limit, 100),
            Offset = offset,
            ParentTicketId = parentTicketId,
            IncludeChildren = !string.IsNullOrEmpty(parentTicketId)
        };

        // Parse status filter
        if (!string.IsNullOrEmpty(status))
        {
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Enum.TryParse<TicketStatus>(s.Trim(), ignoreCase: true, out var result) ? result : (TicketStatus?)null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToArray();

            if (statuses.Length > 0)
            {
                criteria.Statuses = statuses;
            }
        }

        // Parse queue filter
        if (!string.IsNullOrEmpty(queue) && Enum.TryParse<TicketQueue>(queue, ignoreCase: true, out var queueValue))
        {
            criteria.AssignedQueue = queueValue;
        }

        // Parse ticket type filter
        if (!string.IsNullOrEmpty(ticketType) && Enum.TryParse<TicketType>(ticketType, ignoreCase: true, out var typeValue))
        {
            criteria.TicketType = typeValue;
        }

        // Assigned to filter
        if (!string.IsNullOrEmpty(assignedTo))
        {
            criteria.AssignedTo = assignedTo;
        }

        // Authorization: Regular users can only see their own tickets
        if (!userContext.HasElevatedAccess)
        {
            criteria.CreatedBy = userContext.CurrentUserId;
        }

        var tickets = await ticketListPortal.FetchAsync(criteria);

        return new TicketListResult
        {
            Tickets = tickets.Select(t => TicketSummary.FromTicketInfo(t)).ToList(),
            TotalCount = tickets.TotalCount,
            Offset = offset,
            Limit = criteria.Limit,
            HasMore = tickets.HasMore
        };
    }

    /// <summary>
    /// Get detailed information about a specific ticket.
    /// </summary>
    [McpServerTool(Name = "ticket_get")]
    [Description("Get detailed information about a specific ticket by ID.")]
    public async Task<TicketDetailResult> GetTicket(
        [Description("The ticket ID (e.g., TKT-00001)")] string ticketId)
    {
        if (!userContext.IsAuthenticated)
        {
            return new TicketDetailResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return new TicketDetailResult { Error = "Ticket ID is required." };
        }

        try
        {
            var ticket = await ticketEditPortal.FetchAsync(ticketId);

            // Authorization: Regular users can only see their own tickets
            if (!userContext.HasElevatedAccess && ticket.CreatedBy != userContext.CurrentUserId)
            {
                return new TicketDetailResult { Error = $"Access denied. You don't have permission to view ticket {ticketId}." };
            }

            return new TicketDetailResult { Ticket = TicketDetail.FromTicketEdit(ticket) };
        }
        catch (DataNotFoundException)
        {
            return new TicketDetailResult { Error = $"Ticket '{ticketId}' not found." };
        }
    }

    /// <summary>
    /// Create a new ticket on behalf of the authenticated user.
    /// </summary>
    [McpServerTool(Name = "ticket_create")]
    [Description("Create a new ticket. The ticket will be created with the authenticated user as the requestor.")]
    public async Task<TicketDetailResult> CreateTicket(
        [Description("Title of the ticket (required, 1-200 characters)")] string title,
        [Description("Type of ticket: Support, Purchase, or Delivery")] string ticketType,
        [Description("Detailed description of the issue or request (optional, max 2000 characters)")] string? description = null,
        [Description("Category: Hardware, Software, Network, Access, or Other")] string? category = null,
        [Description("Priority: Low, Medium, High, or Critical (default: Medium)")] string? priority = null,
        [Description("Parent ticket ID if this is a related/child ticket")] string? parentTicketId = null)
    {
        if (!userContext.IsAuthenticated)
        {
            return new TicketDetailResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        // Validate ticket type
        if (!Enum.TryParse<TicketType>(ticketType, ignoreCase: true, out var typeValue))
        {
            return new TicketDetailResult { Error = $"Invalid ticket type '{ticketType}'. Valid values: Support, Purchase, Delivery" };
        }

        // Create the ticket
        var ticket = await ticketEditPortal.CreateAsync(userContext.CurrentUserId, userContext.CurrentUserName);

        ticket.Title = title;
        ticket.Description = description;
        ticket.TicketTypeValue = typeValue;

        if (!string.IsNullOrEmpty(category) && Enum.TryParse<TicketCategory>(category, ignoreCase: true, out var categoryValue))
        {
            ticket.Category = categoryValue;
        }

        if (!string.IsNullOrEmpty(priority) && Enum.TryParse<TicketPriority>(priority, ignoreCase: true, out var priorityValue))
        {
            ticket.Priority = priorityValue;
        }

        if (!string.IsNullOrEmpty(parentTicketId))
        {
            ticket.ParentTicketId = parentTicketId;
        }

        // Validate
        if (!ticket.IsValid)
        {
            var errors = ticket.BrokenRulesCollection
                .Select(r => $"{r.Property}: {r.Description}")
                .ToList();
            return new TicketDetailResult { Error = $"Validation failed: {string.Join("; ", errors)}" };
        }

        ticket = await ticket.SaveAsync();

        return new TicketDetailResult 
        { 
            Ticket = TicketDetail.FromTicketEdit(ticket),
            Message = $"Ticket {ticket.TicketId} created successfully."
        };
    }

    /// <summary>
    /// Update an existing ticket.
    /// </summary>
    [McpServerTool(Name = "ticket_update")]
    [Description("Update an existing ticket. Regular users can only update their own tickets with limited fields. HelpDesk and Approver roles can update all fields.")]
    public async Task<TicketDetailResult> UpdateTicket(
        [Description("The ticket ID to update (e.g., TKT-00001)")] string ticketId,
        [Description("New title (1-200 characters)")] string? title = null,
        [Description("New description (max 2000 characters)")] string? description = null,
        [Description("New ticket type: Support, Purchase, or Delivery")] string? ticketType = null,
        [Description("New category: Hardware, Software, Network, Access, or Other")] string? category = null,
        [Description("New priority: Low, Medium, High, or Critical")] string? priority = null,
        [Description("New status (requires elevated access): New, Triaged, InProgress, PendingApproval, Approved, Rejected, PendingFulfillment, Fulfilled, Resolved, Closed")] string? status = null,
        [Description("Queue assignment (requires elevated access): Helpdesk, Purchasing, Fulfillment")] string? assignedQueue = null,
        [Description("User assignment (requires elevated access)")] string? assignedTo = null,
        [Description("Triage decision notes (requires elevated access)")] string? triageDecision = null,
        [Description("Additional triage notes (requires elevated access)")] string? triageNotes = null,
        [Description("Resolution notes (requires elevated access)")] string? resolutionNotes = null,
        [Description("Parent ticket ID for ticket relationships")] string? parentTicketId = null)
    {
        if (!userContext.IsAuthenticated)
        {
            return new TicketDetailResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return new TicketDetailResult { Error = "Ticket ID is required." };
        }

        TicketEdit ticket;
        try
        {
            ticket = await ticketEditPortal.FetchAsync(ticketId);
        }
        catch (DataNotFoundException)
        {
            return new TicketDetailResult { Error = $"Ticket '{ticketId}' not found." };
        }

        var isOwner = ticket.CreatedBy == userContext.CurrentUserId;
        var hasElevatedAccess = userContext.HasElevatedAccess;

        // Authorization: Must be owner or have elevated access
        if (!isOwner && !hasElevatedAccess)
        {
            return new TicketDetailResult { Error = $"Access denied. You don't have permission to update ticket {ticketId}." };
        }

        // Apply updates - regular users have limited update capabilities
        if (title != null) ticket.Title = title;
        if (description != null) ticket.Description = description;

        if (ticketType != null && Enum.TryParse<TicketType>(ticketType, ignoreCase: true, out var typeValue))
        {
            ticket.TicketTypeValue = typeValue;
        }

        if (category != null)
        {
            ticket.Category = Enum.TryParse<TicketCategory>(category, ignoreCase: true, out var categoryValue) 
                ? categoryValue 
                : null;
        }

        if (priority != null && Enum.TryParse<TicketPriority>(priority, ignoreCase: true, out var priorityValue))
        {
            ticket.Priority = priorityValue;
        }

        // Elevated access required for these fields
        if (hasElevatedAccess)
        {
            if (status != null && Enum.TryParse<TicketStatus>(status, ignoreCase: true, out var statusValue))
            {
                ticket.Status = statusValue;
            }

            if (assignedQueue != null)
            {
                ticket.AssignedQueue = Enum.TryParse<TicketQueue>(assignedQueue, ignoreCase: true, out var queueValue) 
                    ? queueValue 
                    : null;
            }

            if (assignedTo != null)
            {
                ticket.AssignedTo = string.IsNullOrEmpty(assignedTo) ? null : assignedTo;
            }

            if (triageDecision != null) ticket.TriageDecision = triageDecision;
            if (triageNotes != null) ticket.TriageNotes = triageNotes;
            if (resolutionNotes != null) ticket.ResolutionNotes = resolutionNotes;
        }

        if (parentTicketId != null)
        {
            ticket.ParentTicketId = string.IsNullOrEmpty(parentTicketId) ? null : parentTicketId;
        }

        // Validate
        if (!ticket.IsValid)
        {
            var errors = ticket.BrokenRulesCollection
                .Select(r => $"{r.Property}: {r.Description}")
                .ToList();
            return new TicketDetailResult { Error = $"Validation failed: {string.Join("; ", errors)}" };
        }

        ticket = await ticket.SaveAsync();

        return new TicketDetailResult 
        { 
            Ticket = TicketDetail.FromTicketEdit(ticket),
            Message = $"Ticket {ticket.TicketId} updated successfully."
        };
    }

    /// <summary>
    /// Get all child tickets for a parent ticket.
    /// </summary>
    [McpServerTool(Name = "ticket_get_children")]
    [Description("Get all child tickets that are linked to a parent ticket. Useful for tracking related work items like delivery tickets for a purchase.")]
    public async Task<TicketListResult> GetChildTickets(
        [Description("The parent ticket ID (e.g., TKT-00001)")] string parentTicketId)
    {
        if (!userContext.IsAuthenticated)
        {
            return new TicketListResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (string.IsNullOrWhiteSpace(parentTicketId))
        {
            return new TicketListResult { Error = "Parent ticket ID is required." };
        }

        var criteria = new TicketListCriteria
        {
            ParentTicketId = parentTicketId,
            IncludeChildren = true,
            Limit = 100
        };

        // Authorization: Regular users can only see their own tickets
        if (!userContext.HasElevatedAccess)
        {
            criteria.CreatedBy = userContext.CurrentUserId;
        }

        var tickets = await ticketListPortal.FetchAsync(criteria);

        return new TicketListResult
        {
            Tickets = tickets.Select(t => TicketSummary.FromTicketInfo(t)).ToList(),
            TotalCount = tickets.TotalCount,
            HasMore = tickets.HasMore,
            ParentTicketId = parentTicketId
        };
    }

    /// <summary>
    /// Close a ticket with resolution notes.
    /// </summary>
    [McpServerTool(Name = "ticket_close")]
    [Description("Close a ticket by setting its status to Closed with optional resolution notes. Requires elevated access (HelpDesk or Approver role).")]
    public async Task<TicketDetailResult> CloseTicket(
        [Description("The ticket ID to close (e.g., TKT-00001)")] string ticketId,
        [Description("Resolution notes explaining how the ticket was resolved")] string? resolutionNotes = null)
    {
        if (!userContext.IsAuthenticated)
        {
            return new TicketDetailResult { Error = "Authentication required. Please provide a valid JWT token." };
        }

        if (!userContext.HasElevatedAccess)
        {
            return new TicketDetailResult { Error = "Access denied. Closing tickets requires HelpDesk or Approver role." };
        }

        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return new TicketDetailResult { Error = "Ticket ID is required." };
        }

        TicketEdit ticket;
        try
        {
            ticket = await ticketEditPortal.FetchAsync(ticketId);
        }
        catch (DataNotFoundException)
        {
            return new TicketDetailResult { Error = $"Ticket '{ticketId}' not found." };
        }

        ticket.Status = TicketStatus.Closed;
        if (!string.IsNullOrEmpty(resolutionNotes))
        {
            ticket.ResolutionNotes = resolutionNotes;
        }

        ticket = await ticket.SaveAsync();

        return new TicketDetailResult 
        { 
            Ticket = TicketDetail.FromTicketEdit(ticket),
            Message = $"Ticket {ticket.TicketId} has been closed."
        };
    }
}

#region Result Models

public class TicketListResult
{
    public string? Error { get; set; }
    public List<TicketSummary> Tickets { get; set; } = [];
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public bool HasMore { get; set; }
    public string? ParentTicketId { get; set; }
}

public class TicketDetailResult
{
    public string? Error { get; set; }
    public string? Message { get; set; }
    public TicketDetail? Ticket { get; set; }
}

public class TicketSummary
{
    public required string TicketId { get; set; }
    public required string Title { get; set; }
    public required string TicketType { get; set; }
    public string? Category { get; set; }
    public required string Priority { get; set; }
    public required string Status { get; set; }
    public string? AssignedQueue { get; set; }
    public string? AssignedTo { get; set; }
    public required string CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ParentTicketId { get; set; }

    public static TicketSummary FromTicketInfo(TicketInfo ticket) => new()
    {
        TicketId = ticket.TicketId,
        Title = ticket.Title,
        TicketType = ticket.TicketTypeValue.ToString(),
        Category = ticket.Category?.ToString(),
        Priority = ticket.Priority.ToString(),
        Status = ticket.Status.ToString(),
        AssignedQueue = ticket.AssignedQueue?.ToString(),
        AssignedTo = ticket.AssignedTo,
        CreatedBy = ticket.CreatedBy,
        CreatedByName = ticket.CreatedByName,
        CreatedAt = ticket.CreatedAt,
        UpdatedAt = ticket.UpdatedAt,
        ParentTicketId = ticket.ParentTicketId
    };
}

public class TicketDetail
{
    public required string TicketId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string TicketType { get; set; }
    public string? Category { get; set; }
    public required string Priority { get; set; }
    public required string Status { get; set; }
    public string? AssignedQueue { get; set; }
    public string? AssignedTo { get; set; }
    public required string CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? TriageDecision { get; set; }
    public string? TriageNotes { get; set; }
    public string? ResolutionNotes { get; set; }
    public string? ParentTicketId { get; set; }

    public static TicketDetail FromTicketEdit(TicketEdit ticket) => new()
    {
        TicketId = ticket.TicketId,
        Title = ticket.Title,
        Description = ticket.Description,
        TicketType = ticket.TicketTypeValue.ToString(),
        Category = ticket.Category?.ToString(),
        Priority = ticket.Priority.ToString(),
        Status = ticket.Status.ToString(),
        AssignedQueue = ticket.AssignedQueue?.ToString(),
        AssignedTo = ticket.AssignedTo,
        CreatedBy = ticket.CreatedBy,
        CreatedByName = ticket.CreatedByName,
        CreatedAt = ticket.CreatedAt,
        UpdatedAt = ticket.UpdatedAt,
        ClosedAt = ticket.ClosedAt,
        TriageDecision = ticket.TriageDecision,
        TriageNotes = ticket.TriageNotes,
        ResolutionNotes = ticket.ResolutionNotes,
        ParentTicketId = ticket.ParentTicketId
    };
}

#endregion
