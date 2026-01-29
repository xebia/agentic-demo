using Csla;
using Csla.Serialization;
using Ticketing.DataAccess.Abstractions;

namespace Ticketing.Domain;

/// <summary>
/// Criteria for filtering ticket lists.
/// </summary>
[AutoSerializable]
public partial class TicketListCriteria
{
    public TicketStatus[]? Statuses { get; set; }
    public TicketQueue? AssignedQueue { get; set; }
    public string? AssignedTo { get; set; }
    public string? CreatedBy { get; set; }
    public string? ParentTicketId { get; set; }
    public TicketType? TicketType { get; set; }
    public bool IncludeChildren { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; } = 0;

    /// <summary>
    /// Creates criteria for open help desk tickets.
    /// </summary>
    public static TicketListCriteria OpenHelpDeskTickets() => new()
    {
        Statuses = [TicketStatus.New, TicketStatus.Triaged, TicketStatus.InProgress],
        AssignedQueue = Domain.TicketQueue.Helpdesk
    };

    /// <summary>
    /// Creates criteria for closed help desk tickets.
    /// </summary>
    public static TicketListCriteria ClosedHelpDeskTickets() => new()
    {
        Statuses = [TicketStatus.Resolved, TicketStatus.Closed],
        AssignedQueue = Domain.TicketQueue.Helpdesk
    };

    /// <summary>
    /// Creates criteria for all open tickets (read-only view).
    /// </summary>
    public static TicketListCriteria AllOpenTickets() => new()
    {
        Statuses = [
            TicketStatus.New,
            TicketStatus.Triaged,
            TicketStatus.InProgress,
            TicketStatus.PendingApproval,
            TicketStatus.Approved,
            TicketStatus.PendingFulfillment
        ]
    };

    /// <summary>
    /// Creates criteria for tickets pending approval.
    /// </summary>
    public static TicketListCriteria PendingApproval() => new()
    {
        Statuses = [TicketStatus.PendingApproval],
        AssignedQueue = Domain.TicketQueue.Purchasing
    };

    /// <summary>
    /// Converts to DAL criteria DTO.
    /// </summary>
    internal TicketListCriteriaDto ToDto() => new()
    {
        Statuses = Statuses?.Select(s => s.ToDbValue()).ToArray(),
        AssignedQueue = AssignedQueue?.ToDbValue(),
        AssignedTo = AssignedTo,
        CreatedBy = CreatedBy,
        ParentTicketId = ParentTicketId,
        TicketType = TicketType?.ToDbValue(),
        IncludeChildren = IncludeChildren,
        Limit = Limit,
        Offset = Offset
    };
}

/// <summary>
/// Read-only list of tickets using CSLA 10 patterns.
/// </summary>
[Serializable]
public class TicketList : ReadOnlyListBase<TicketList, TicketInfo>
{
    /// <summary>
    /// Total count of tickets matching the criteria (for pagination).
    /// </summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Whether there are more tickets available.
    /// </summary>
    public bool HasMore => TotalCount > (_criteria?.Offset ?? 0) + Count;

    private TicketListCriteria? _criteria;

    [Fetch]
    private async Task Fetch(
        TicketListCriteria criteria, 
        [Inject] ITicketListDal dal,
        [Inject] IChildDataPortal<TicketInfo> childPortal)
    {
        _criteria = criteria;
        var (items, totalCount) = dal.Fetch(criteria.ToDto());
        TotalCount = totalCount;

        using (LoadListMode)
        {
            foreach (var data in items)
            {
                // Pass data directly to child - avoids N+1 query problem
                var item = await childPortal.FetchChildAsync(data);
                Add(item);
            }
        }
        
        IsReadOnly = true;
    }
}
