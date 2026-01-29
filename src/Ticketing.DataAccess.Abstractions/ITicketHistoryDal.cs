namespace Ticketing.DataAccess.Abstractions;

/// <summary>
/// DAL interface for ticket history operations.
/// </summary>
public interface ITicketHistoryDal
{
    /// <summary>
    /// Inserts a new history entry.
    /// </summary>
    void Insert(TicketHistoryDto dto);
    
    /// <summary>
    /// Fetches all history entries for a ticket.
    /// </summary>
    IEnumerable<TicketHistoryDto> FetchByTicketId(string ticketId);
}
