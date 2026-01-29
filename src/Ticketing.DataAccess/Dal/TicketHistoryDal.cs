using Ticketing.DataAccess.Abstractions;
using Ticketing.DataAccess.Entities;

namespace Ticketing.DataAccess.Dal;

/// <summary>
/// Entity Framework implementation of ITicketHistoryDal.
/// </summary>
public class TicketHistoryDal : ITicketHistoryDal
{
    private readonly TicketingDbContext _context;

    public TicketHistoryDal(TicketingDbContext context)
    {
        _context = context;
    }

    public void Insert(TicketHistoryDto dto)
    {
        var entity = new TicketHistoryEntity
        {
            TicketId = dto.TicketId,
            FieldName = dto.FieldName,
            OldValue = dto.OldValue,
            NewValue = dto.NewValue,
            ChangedBy = dto.ChangedBy,
            ChangedAt = dto.ChangedAt,
            ChangeReason = dto.ChangeReason
        };

        _context.TicketHistory.Add(entity);
        _context.SaveChanges();
    }

    public IEnumerable<TicketHistoryDto> FetchByTicketId(string ticketId)
    {
        return _context.TicketHistory
            .Where(h => h.TicketId == ticketId)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new TicketHistoryDto(
                h.TicketId,
                h.FieldName,
                h.OldValue,
                h.NewValue,
                h.ChangedBy,
                h.ChangedAt,
                h.ChangeReason))
            .ToList();
    }
}
