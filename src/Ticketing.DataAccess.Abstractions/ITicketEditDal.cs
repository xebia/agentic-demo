namespace Ticketing.DataAccess.Abstractions;

/// <summary>
/// DAL interface for ticket edit operations.
/// </summary>
public interface ITicketEditDal
{
    /// <summary>
    /// Generates a new unique ticket ID.
    /// </summary>
    string GenerateTicketId();
    
    /// <summary>
    /// Fetches a ticket by ID.
    /// </summary>
    TicketEditDto? Fetch(string ticketId);
    
    /// <summary>
    /// Inserts a new ticket.
    /// </summary>
    void Insert(TicketEditDto dto);
    
    /// <summary>
    /// Updates an existing ticket.
    /// </summary>
    void Update(TicketEditDto dto);
    
    /// <summary>
    /// Deletes a ticket by ID.
    /// </summary>
    void Delete(string ticketId);
}
