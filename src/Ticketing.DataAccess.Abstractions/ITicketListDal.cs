namespace Ticketing.DataAccess.Abstractions;

/// <summary>
/// DAL interface for ticket list operations.
/// </summary>
public interface ITicketListDal
{
    /// <summary>
    /// Fetches tickets matching the specified criteria.
    /// </summary>
    /// <param name="criteria">Filter criteria for the query.</param>
    /// <returns>A tuple containing the matching items and total count.</returns>
    (IEnumerable<TicketListItemDto> Items, int TotalCount) Fetch(TicketListCriteriaDto criteria);
}
