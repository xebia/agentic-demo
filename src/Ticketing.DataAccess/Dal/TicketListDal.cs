using Microsoft.EntityFrameworkCore;
using Ticketing.DataAccess.Abstractions;
using Ticketing.DataAccess.Entities;
using Ticketing.DataAccess.Services;

namespace Ticketing.DataAccess.Dal;

/// <summary>
/// Entity Framework implementation of ITicketListDal.
/// </summary>
public class TicketListDal : ITicketListDal
{
    private readonly TicketingDbContext _context;

    public TicketListDal(TicketingDbContext context)
    {
        _context = context;
    }

    public (IEnumerable<TicketListItemDto> Items, int TotalCount) Fetch(TicketListCriteriaDto criteria)
    {
        var query = _context.Tickets.AsNoTracking().AsQueryable();

        // Apply filters
        if (criteria.Statuses?.Length > 0)
        {
            query = query.Where(t => criteria.Statuses.Contains(t.Status));
        }

        if (!string.IsNullOrEmpty(criteria.AssignedQueue))
        {
            query = query.Where(t => t.AssignedQueue == criteria.AssignedQueue);
        }

        if (!string.IsNullOrEmpty(criteria.AssignedTo))
        {
            query = query.Where(t => t.AssignedTo == criteria.AssignedTo);
        }

        if (!string.IsNullOrEmpty(criteria.CreatedBy))
        {
            query = query.Where(t => t.CreatedBy == criteria.CreatedBy);
        }

        if (!string.IsNullOrEmpty(criteria.ParentTicketId))
        {
            query = query.Where(t => t.ParentTicketId == criteria.ParentTicketId);
        }

        if (!string.IsNullOrEmpty(criteria.TicketType))
        {
            query = query.Where(t => t.TicketType == criteria.TicketType);
        }

        // Get total count before pagination
        var totalCount = query.Count();

        // Apply ordering and pagination
        var items = query
            .OrderByDescending(t => t.Priority == "critical")
            .ThenByDescending(t => t.Priority == "high")
            .ThenByDescending(t => t.CreatedAt)
            .Skip(criteria.Offset)
            .Take(criteria.Limit)
            .Select(t => new TicketListItemDto(
                t.TicketId,
                t.Title,
                t.TicketType,
                t.Category,
                t.Priority,
                t.Status,
                t.AssignedQueue,
                t.AssignedTo,
                t.CreatedBy,
                t.CreatedByName,
                t.CreatedAt,
                t.UpdatedAt,
                t.ClosedAt,
                t.ParentTicketId))
            .ToList();

        return (items, totalCount);
    }
}
