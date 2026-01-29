using Ticketing.Domain;

namespace Ticketing.Web.Controllers.Models;

/// <summary>
/// HATEOAS link for REST API responses.
/// </summary>
public class ApiLink
{
    public required string Href { get; set; }
    public string? Method { get; set; }
}

/// <summary>
/// Collection of HATEOAS links.
/// </summary>
public class ApiLinks
{
    public ApiLink? Self { get; set; }
    public ApiLink? ParentTicket { get; set; }
    public ApiLink? Update { get; set; }
}

/// <summary>
/// Response model for a ticket in a list.
/// </summary>
public class TicketListItemResponse
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
    public required ApiLinks Links { get; set; }

    public static TicketListItemResponse FromTicketInfo(TicketInfo ticket, string baseUrl)
    {
        var links = new ApiLinks
        {
            Self = new ApiLink { Href = $"{baseUrl}/{ticket.TicketId}" }
        };

        if (!string.IsNullOrEmpty(ticket.ParentTicketId))
        {
            links.ParentTicket = new ApiLink { Href = $"{baseUrl}/{ticket.ParentTicketId}" };
        }

        return new TicketListItemResponse
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
            ParentTicketId = ticket.ParentTicketId,
            Links = links
        };
    }
}

/// <summary>
/// Response model for a paginated list of tickets.
/// </summary>
public class TicketListResponse
{
    public required List<TicketListItemResponse> Items { get; set; }
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Detailed response model for a single ticket.
/// </summary>
public class TicketDetailResponse
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
    public required ApiLinks Links { get; set; }

    public static TicketDetailResponse FromTicketEdit(TicketEdit ticket, string baseUrl)
    {
        var links = new ApiLinks
        {
            Self = new ApiLink { Href = $"{baseUrl}/{ticket.TicketId}" },
            Update = new ApiLink { Href = $"{baseUrl}/{ticket.TicketId}", Method = "PUT" }
        };

        if (!string.IsNullOrEmpty(ticket.ParentTicketId))
        {
            links.ParentTicket = new ApiLink { Href = $"{baseUrl}/{ticket.ParentTicketId}" };
        }

        return new TicketDetailResponse
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
            ParentTicketId = ticket.ParentTicketId,
            Links = links
        };
    }
}

/// <summary>
/// Request model for creating a new ticket.
/// </summary>
public class CreateTicketRequest
{
    /// <summary>
    /// Title of the ticket (required, 1-200 characters).
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Detailed description of the issue or request (optional, max 2000 characters).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Type of ticket: Support, ServiceRequest, or PurchaseRequest.
    /// </summary>
    public required string TicketType { get; set; }

    /// <summary>
    /// Category of the ticket (optional): Hardware, Software, Network, Access, or Other.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Priority level: Low, Medium, High, or Critical. Defaults to Medium if not specified.
    /// </summary>
    public string? Priority { get; set; }

    /// <summary>
    /// Parent ticket ID if this ticket is related to another ticket (optional).
    /// </summary>
    public string? ParentTicketId { get; set; }
}

/// <summary>
/// Request model for updating an existing ticket.
/// </summary>
public class UpdateTicketRequest
{
    /// <summary>
    /// Title of the ticket (1-200 characters).
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Detailed description of the issue or request (max 2000 characters).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Type of ticket: Support, ServiceRequest, or PurchaseRequest.
    /// </summary>
    public string? TicketType { get; set; }

    /// <summary>
    /// Category: Hardware, Software, Network, Access, or Other.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Priority level: Low, Medium, High, or Critical.
    /// </summary>
    public string? Priority { get; set; }

    /// <summary>
    /// Status of the ticket.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Queue the ticket is assigned to: Helpdesk, Purchasing, or Fulfillment.
    /// </summary>
    public string? AssignedQueue { get; set; }

    /// <summary>
    /// User the ticket is assigned to.
    /// </summary>
    public string? AssignedTo { get; set; }

    /// <summary>
    /// Triage decision notes.
    /// </summary>
    public string? TriageDecision { get; set; }

    /// <summary>
    /// Additional triage notes.
    /// </summary>
    public string? TriageNotes { get; set; }

    /// <summary>
    /// Resolution notes (for closing tickets).
    /// </summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// Parent ticket ID for ticket relationships.
    /// </summary>
    public string? ParentTicketId { get; set; }
}
