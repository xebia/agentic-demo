using Ticketing.DataAccess.Abstractions;
using Ticketing.DataAccess.Entities;
using Ticketing.DataAccess.Services;

namespace Ticketing.DataAccess.Dal;

/// <summary>
/// Entity Framework implementation of ITicketEditDal.
/// </summary>
public class TicketEditDal : ITicketEditDal
{
    private readonly TicketingDbContext _context;
    private readonly ITicketIdGenerator _idGenerator;

    public TicketEditDal(TicketingDbContext context, ITicketIdGenerator idGenerator)
    {
        _context = context;
        _idGenerator = idGenerator;
    }

    public string GenerateTicketId()
    {
        return _idGenerator.GenerateTicketIdAsync().GetAwaiter().GetResult();
    }

    public TicketEditDto? Fetch(string ticketId)
    {
        var entity = _context.Tickets.Find(ticketId);
        if (entity == null) return null;

        return new TicketEditDto(
            entity.TicketId,
            entity.Title,
            entity.Description,
            entity.TicketType,
            entity.Category,
            entity.Priority,
            entity.Status,
            entity.AssignedQueue,
            entity.AssignedTo,
            entity.CreatedBy,
            entity.CreatedByName,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.ClosedAt,
            entity.TriageDecision,
            entity.TriageNotes,
            entity.ResolutionNotes,
            entity.ParentTicketId);
    }

    public void Insert(TicketEditDto dto)
    {
        var entity = new TicketEntity
        {
            TicketId = dto.TicketId,
            Title = dto.Title,
            Description = dto.Description,
            TicketType = dto.TicketType,
            Category = dto.Category,
            Priority = dto.Priority,
            Status = dto.Status,
            AssignedQueue = dto.AssignedQueue,
            AssignedTo = dto.AssignedTo,
            CreatedBy = dto.CreatedBy,
            CreatedByName = dto.CreatedByName,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            ClosedAt = dto.ClosedAt,
            TriageDecision = dto.TriageDecision,
            TriageNotes = dto.TriageNotes,
            ResolutionNotes = dto.ResolutionNotes,
            ParentTicketId = dto.ParentTicketId
        };

        _context.Tickets.Add(entity);
        _context.SaveChanges();
    }

    public void Update(TicketEditDto dto)
    {
        var entity = _context.Tickets.Find(dto.TicketId);
        if (entity == null)
        {
            throw new InvalidOperationException($"Ticket {dto.TicketId} not found.");
        }

        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.TicketType = dto.TicketType;
        entity.Category = dto.Category;
        entity.Priority = dto.Priority;
        entity.Status = dto.Status;
        entity.AssignedQueue = dto.AssignedQueue;
        entity.AssignedTo = dto.AssignedTo;
        entity.UpdatedAt = dto.UpdatedAt;
        entity.ClosedAt = dto.ClosedAt;
        entity.TriageDecision = dto.TriageDecision;
        entity.TriageNotes = dto.TriageNotes;
        entity.ResolutionNotes = dto.ResolutionNotes;
        entity.ParentTicketId = dto.ParentTicketId;

        _context.SaveChanges();
    }

    public void Delete(string ticketId)
    {
        var entity = _context.Tickets.Find(ticketId);
        if (entity != null)
        {
            _context.Tickets.Remove(entity);
            _context.SaveChanges();
        }
    }
}
