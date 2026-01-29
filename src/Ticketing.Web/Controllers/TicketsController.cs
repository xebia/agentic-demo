using Csla;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Domain;
using Ticketing.Web.Controllers.Models;
using Ticketing.Web.Services.Auth;

namespace Ticketing.Web.Controllers;

/// <summary>
/// REST API controller for ticket operations.
/// All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class TicketsController : ControllerBase
{
    private readonly IDataPortal<TicketList> _ticketListPortal;
    private readonly IDataPortal<TicketEdit> _ticketEditPortal;
    private readonly ApiUserContext _userContext;

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}/api/tickets";

    public TicketsController(
        IDataPortal<TicketList> ticketListPortal,
        IDataPortal<TicketEdit> ticketEditPortal,
        ApiUserContext userContext)
    {
        _ticketListPortal = ticketListPortal;
        _ticketEditPortal = ticketEditPortal;
        _userContext = userContext;
    }

    /// <summary>
    /// Get a list of tickets.
    /// Regular users see only their own tickets. HelpDesk and Approver roles see all tickets.
    /// </summary>
    /// <param name="status">Filter by status (comma-separated for multiple)</param>
    /// <param name="queue">Filter by assigned queue</param>
    /// <param name="assignedTo">Filter by assigned user</param>
    /// <param name="ticketType">Filter by ticket type</param>
    /// <param name="limit">Maximum number of results (default 50, max 100)</param>
    /// <param name="offset">Number of results to skip for pagination</param>
    [HttpGet]
    [ProducesResponseType<TicketListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketListResponse>> GetTickets(
        [FromQuery] string? status = null,
        [FromQuery] string? queue = null,
        [FromQuery] string? assignedTo = null,
        [FromQuery] string? ticketType = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var criteria = new TicketListCriteria
        {
            Limit = Math.Min(limit, 100),
            Offset = offset
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
        if (!_userContext.HasElevatedAccess)
        {
            criteria.CreatedBy = _userContext.CurrentUserId;
        }

        var tickets = await _ticketListPortal.FetchAsync(criteria);

        var response = new TicketListResponse
        {
            Items = tickets.Select(t => TicketListItemResponse.FromTicketInfo(t, BaseUrl)).ToList(),
            TotalCount = tickets.TotalCount,
            Offset = offset,
            Limit = criteria.Limit,
            HasMore = tickets.HasMore
        };

        return Ok(response);
    }

    /// <summary>
    /// Get a single ticket by ID.
    /// Regular users can only access tickets they created.
    /// </summary>
    [HttpGet("{ticketId}")]
    [ProducesResponseType<TicketDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailResponse>> GetTicket(string ticketId)
    {
        try
        {
            var ticket = await _ticketEditPortal.FetchAsync(ticketId);

            // Authorization: Regular users can only see their own tickets
            if (!_userContext.HasElevatedAccess && ticket.CreatedBy != _userContext.CurrentUserId)
            {
                return Forbid();
            }

            return Ok(TicketDetailResponse.FromTicketEdit(ticket, BaseUrl));
        }
        catch (DataNotFoundException)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Ticket not found",
                Detail = $"No ticket found with ID '{ticketId}'",
                Status = StatusCodes.Status404NotFound
            });
        }
    }

    /// <summary>
    /// Create a new ticket.
    /// The ticket will be created with the authenticated user as the requestor.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<TicketDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailResponse>> CreateTicket([FromBody] CreateTicketRequest request)
    {
        // Parse and validate ticket type
        if (!Enum.TryParse<TicketType>(request.TicketType, ignoreCase: true, out var ticketType))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid ticket type",
                Detail = $"'{request.TicketType}' is not a valid ticket type. Valid values: {string.Join(", ", Enum.GetNames<TicketType>())}",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Create new ticket via CSLA data portal
        var ticket = await _ticketEditPortal.CreateAsync(_userContext.CurrentUserId, _userContext.CurrentUserName);

        // Set required fields
        ticket.Title = request.Title;
        ticket.Description = request.Description;
        ticket.TicketTypeValue = ticketType;

        // Set optional fields
        if (!string.IsNullOrEmpty(request.Category) && 
            Enum.TryParse<TicketCategory>(request.Category, ignoreCase: true, out var category))
        {
            ticket.Category = category;
        }

        if (!string.IsNullOrEmpty(request.Priority) && 
            Enum.TryParse<TicketPriority>(request.Priority, ignoreCase: true, out var priority))
        {
            ticket.Priority = priority;
        }

        if (!string.IsNullOrEmpty(request.ParentTicketId))
        {
            ticket.ParentTicketId = request.ParentTicketId;
        }

        // Validate via CSLA business rules
        if (!ticket.IsValid)
        {
            var errors = ticket.BrokenRulesCollection
                .Select(r => $"{r.Property}: {r.Description}")
                .ToList();

            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = string.Join("; ", errors),
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Save the ticket
        ticket = await ticket.SaveAsync();

        var response = TicketDetailResponse.FromTicketEdit(ticket, BaseUrl);
        return CreatedAtAction(nameof(GetTicket), new { ticketId = ticket.TicketId }, response);
    }

    /// <summary>
    /// Update an existing ticket.
    /// Regular users can only update tickets they created (limited fields).
    /// HelpDesk and Approver roles can update all fields.
    /// </summary>
    [HttpPut("{ticketId}")]
    [ProducesResponseType<TicketDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailResponse>> UpdateTicket(string ticketId, [FromBody] UpdateTicketRequest request)
    {
        TicketEdit ticket;
        try
        {
            ticket = await _ticketEditPortal.FetchAsync(ticketId);
        }
        catch (DataNotFoundException)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Ticket not found",
                Detail = $"No ticket found with ID '{ticketId}'",
                Status = StatusCodes.Status404NotFound
            });
        }

        var isOwner = ticket.CreatedBy == _userContext.CurrentUserId;
        var hasElevatedAccess = _userContext.HasElevatedAccess;

        // Authorization: Must be owner or have elevated access
        if (!isOwner && !hasElevatedAccess)
        {
            return Forbid();
        }

        // Apply updates - regular users have limited update capabilities
        if (request.Title != null)
        {
            ticket.Title = request.Title;
        }

        if (request.Description != null)
        {
            ticket.Description = request.Description;
        }

        if (request.TicketType != null && 
            Enum.TryParse<TicketType>(request.TicketType, ignoreCase: true, out var ticketType))
        {
            ticket.TicketTypeValue = ticketType;
        }

        if (request.Category != null)
        {
            ticket.Category = string.IsNullOrEmpty(request.Category) 
                ? null 
                : Enum.TryParse<TicketCategory>(request.Category, ignoreCase: true, out var category) 
                    ? category 
                    : null;
        }

        if (request.Priority != null && 
            Enum.TryParse<TicketPriority>(request.Priority, ignoreCase: true, out var priority))
        {
            ticket.Priority = priority;
        }

        // Elevated access required for these fields
        if (hasElevatedAccess)
        {
            if (request.Status != null && 
                Enum.TryParse<TicketStatus>(request.Status, ignoreCase: true, out var status))
            {
                ticket.Status = status;
            }

            if (request.AssignedQueue != null)
            {
                ticket.AssignedQueue = string.IsNullOrEmpty(request.AssignedQueue)
                    ? null
                    : Enum.TryParse<TicketQueue>(request.AssignedQueue, ignoreCase: true, out var queue)
                        ? queue
                        : null;
            }

            if (request.AssignedTo != null)
            {
                ticket.AssignedTo = string.IsNullOrEmpty(request.AssignedTo) ? null : request.AssignedTo;
            }

            if (request.TriageDecision != null)
            {
                ticket.TriageDecision = request.TriageDecision;
            }

            if (request.TriageNotes != null)
            {
                ticket.TriageNotes = request.TriageNotes;
            }

            if (request.ResolutionNotes != null)
            {
                ticket.ResolutionNotes = request.ResolutionNotes;
            }

            if (request.ParentTicketId != null)
            {
                ticket.ParentTicketId = string.IsNullOrEmpty(request.ParentTicketId) ? null : request.ParentTicketId;
            }
        }

        // Validate via CSLA business rules
        if (!ticket.IsValid)
        {
            var errors = ticket.BrokenRulesCollection
                .Select(r => $"{r.Property}: {r.Description}")
                .ToList();

            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = string.Join("; ", errors),
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Save the ticket
        ticket = await ticket.SaveAsync();

        return Ok(TicketDetailResponse.FromTicketEdit(ticket, BaseUrl));
    }
}
