using Csla;
using Ticketing.DataAccess.Abstractions;

namespace Ticketing.Domain;

/// <summary>
/// Read-only ticket information for list displays using CSLA 10 partial property implementation.
/// </summary>
[Serializable]
[CslaImplementProperties]
public partial class TicketInfo : ReadOnlyBase<TicketInfo>
{
    #region Properties

    public partial string TicketId { get; private set; }

    public partial string Title { get; private set; }

    public partial TicketType TicketTypeValue { get; private set; }

    public partial TicketCategory? Category { get; private set; }

    public partial TicketPriority Priority { get; private set; }

    public partial TicketStatus Status { get; private set; }

    public partial TicketQueue? AssignedQueue { get; private set; }

    public partial string? AssignedTo { get; private set; }

    public partial string CreatedBy { get; private set; }

    public partial string? CreatedByName { get; private set; }

    public partial DateTime CreatedAt { get; private set; }

    public partial DateTime UpdatedAt { get; private set; }

    public partial DateTime? ClosedAt { get; private set; }

    public partial string? ParentTicketId { get; private set; }

    #endregion

    #region Calculated Properties

    /// <summary>
    /// Display name for the requestor.
    /// </summary>
    public string RequestorDisplayName => CreatedByName ?? CreatedBy;

    /// <summary>
    /// Time since the ticket was created.
    /// </summary>
    public string TimeSinceCreation
    {
        get
        {
            var timeSpan = DateTime.UtcNow - CreatedAt;
            return timeSpan switch
            {
                { TotalMinutes: < 1 } => "just now",
                { TotalMinutes: < 60 } => $"{(int)timeSpan.TotalMinutes}m ago",
                { TotalHours: < 24 } => $"{(int)timeSpan.TotalHours}h ago",
                { TotalDays: < 30 } => $"{(int)timeSpan.TotalDays}d ago",
                _ => CreatedAt.ToShortDateString()
            };
        }
    }

    /// <summary>
    /// CSS class for priority badge styling.
    /// </summary>
    public string PriorityCssClass => Priority switch
    {
        TicketPriority.Critical => "badge bg-danger",
        TicketPriority.High => "badge bg-warning text-dark",
        TicketPriority.Medium => "badge bg-info text-dark",
        TicketPriority.Low => "badge bg-success",
        _ => "badge bg-secondary"
    };

    #endregion

    #region Data Portal Operations

    [FetchChild]
    private void FetchChild(TicketListItemDto data)
    {
        LoadProperty(TicketIdProperty, data.TicketId);
        LoadProperty(TitleProperty, data.Title);
        LoadProperty(TicketTypeValueProperty, data.TicketType.ToTicketType());
        LoadProperty(CategoryProperty, data.Category.ToTicketCategory());
        LoadProperty(PriorityProperty, data.Priority.ToTicketPriority());
        LoadProperty(StatusProperty, data.Status.ToTicketStatus());
        LoadProperty(AssignedQueueProperty, data.AssignedQueue.ToTicketQueue());
        LoadProperty(AssignedToProperty, data.AssignedTo);
        LoadProperty(CreatedByProperty, data.CreatedBy);
        LoadProperty(CreatedByNameProperty, data.CreatedByName);
        LoadProperty(CreatedAtProperty, data.CreatedAt);
        LoadProperty(UpdatedAtProperty, data.UpdatedAt);
        LoadProperty(ClosedAtProperty, data.ClosedAt);
        LoadProperty(ParentTicketIdProperty, data.ParentTicketId);
    }

    #endregion
}
