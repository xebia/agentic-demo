using System.ComponentModel.DataAnnotations;
using Csla;
using Ticketing.DataAccess.Abstractions;
using Ticketing.Domain.Services;
using Ticketing.Messaging.Abstractions;

namespace Ticketing.Domain;

/// <summary>
/// Editable ticket business object using CSLA 10 partial property implementation.
/// </summary>
[Serializable]
[CslaImplementProperties]
public partial class TicketEdit : BusinessBase<TicketEdit>
{
    #region Properties

    // Primary key - read-only
    public partial string TicketId { get; private set; }

    // User-editable properties
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public partial string Title { get; set; }

    [StringLength(2000)]
    public partial string? Description { get; set; }

    [Required]
    public partial TicketType TicketTypeValue { get; set; }

    public partial TicketCategory? Category { get; set; }

    [Required]
    public partial TicketPriority Priority { get; set; }

    [Required]
    public partial TicketStatus Status { get; set; }

    public partial TicketQueue? AssignedQueue { get; set; }

    public partial string? AssignedTo { get; set; }

    // Audit fields - read-only
    public partial string CreatedBy { get; private set; }

    public partial string? CreatedByName { get; set; }

    public partial DateTime CreatedAt { get; private set; }

    public partial DateTime UpdatedAt { get; private set; }

    public partial DateTime? ClosedAt { get; private set; }

    // Triage and resolution fields
    [StringLength(500)]
    public partial string? TriageDecision { get; set; }

    [StringLength(500)]
    public partial string? TriageNotes { get; set; }

    [StringLength(2000)]
    public partial string? ResolutionNotes { get; set; }

    // Parent ticket reference
    public partial string? ParentTicketId { get; set; }

    #endregion

    #region Factory Methods

    [Create]
    private void Create([Inject] ITicketEditDal dal, string createdBy, string? createdByName = null)
    {
        LoadProperty(TicketIdProperty, dal.GenerateTicketId());
        LoadProperty(CreatedByProperty, createdBy);
        LoadProperty(CreatedByNameProperty, createdByName);
        LoadProperty(CreatedAtProperty, DateTime.UtcNow);
        LoadProperty(UpdatedAtProperty, DateTime.UtcNow);
        LoadProperty(StatusProperty, TicketStatus.New);
        LoadProperty(PriorityProperty, TicketPriority.Medium);
        LoadProperty(TicketTypeValueProperty, TicketType.Support);
        LoadProperty(ParentTicketIdProperty, (string?)null);
        BusinessRules.CheckRules();
    }

    [Fetch]
    private void Fetch(string ticketId, [Inject] ITicketEditDal dal)
    {
        var data = dal.Fetch(ticketId);
        if (data == null)
        {
            throw new DataNotFoundException($"Ticket {ticketId} not found.");
        }

        LoadData(data);
    }

    private void LoadData(TicketEditDto data)
    {
        LoadProperty(TicketIdProperty, data.TicketId);
        LoadProperty(TitleProperty, data.Title);
        LoadProperty(DescriptionProperty, data.Description);
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
        LoadProperty(TriageDecisionProperty, data.TriageDecision);
        LoadProperty(TriageNotesProperty, data.TriageNotes);
        LoadProperty(ResolutionNotesProperty, data.ResolutionNotes);
        LoadProperty(ParentTicketIdProperty, data.ParentTicketId);

        BusinessRules.CheckRules();
    }

    #endregion

    #region Data Access

    [Insert]
    private async Task Insert([Inject] ITicketEditDal dal, [Inject] ITicketHistoryDal historyDal, [Inject] IUserContext userContext, [Inject] IEventPublisher eventPublisher)
    {
        LoadProperty(UpdatedAtProperty, DateTime.UtcNow);

        var dto = CreateDto();
        dal.Insert(dto);

        // Record creation in history
        historyDal.Insert(new TicketHistoryDto(
            ReadProperty(TicketIdProperty)!,
            "Created",
            null,
            $"Ticket created: {ReadProperty(TitleProperty)}",
            userContext.CurrentUserId,
            DateTime.UtcNow,
            "New ticket created"));

        // Publish ticket.created event for downstream agents
        await eventPublisher.PublishAsync(new TicketEvent
        {
            EventType = TicketEventTypes.TicketCreated,
            Payload = new TicketEventPayload
            {
                TicketId = ReadProperty(TicketIdProperty)!,
                Title = ReadProperty(TitleProperty)!,
                Status = ReadProperty(StatusProperty).ToString(),
                Priority = ReadProperty(PriorityProperty).ToString(),
                Category = ReadProperty(CategoryProperty)?.ToString(),
                CreatedBy = ReadProperty(CreatedByProperty)!
            }
        });
    }

    [Update]
    private void Update([Inject] ITicketEditDal dal, [Inject] ITicketHistoryDal historyDal, [Inject] IUserContext userContext)
    {
        LoadProperty(UpdatedAtProperty, DateTime.UtcNow);
        
        // Get old values for history
        var oldData = dal.Fetch(ReadProperty(TicketIdProperty)!);
        
        var dto = CreateDto();
        dal.Update(dto);

        // Record changes in history
        if (oldData != null)
        {
            RecordChanges(historyDal, oldData, userContext.CurrentUserId);
        }
    }

    [Delete]
    private void Delete(string ticketId, [Inject] ITicketEditDal dal)
    {
        dal.Delete(ticketId);
    }

    private TicketEditDto CreateDto() => new(
        ReadProperty(TicketIdProperty)!,
        ReadProperty(TitleProperty)!,
        ReadProperty(DescriptionProperty),
        ReadProperty(TicketTypeValueProperty).ToDbValue(),
        ReadProperty(CategoryProperty)?.ToDbValue(),
        ReadProperty(PriorityProperty).ToDbValue(),
        ReadProperty(StatusProperty).ToDbValue(),
        ReadProperty(AssignedQueueProperty)?.ToDbValue(),
        ReadProperty(AssignedToProperty),
        ReadProperty(CreatedByProperty)!,
        ReadProperty(CreatedByNameProperty),
        ReadProperty(CreatedAtProperty),
        ReadProperty(UpdatedAtProperty),
        ReadProperty(ClosedAtProperty),
        ReadProperty(TriageDecisionProperty),
        ReadProperty(TriageNotesProperty),
        ReadProperty(ResolutionNotesProperty),
        ReadProperty(ParentTicketIdProperty));

    private void RecordChanges(ITicketHistoryDal historyDal, TicketEditDto oldData, string changedBy)
    {
        var now = DateTime.UtcNow;
        var ticketId = ReadProperty(TicketIdProperty)!;
        var status = ReadProperty(StatusProperty);
        var assignedQueue = ReadProperty(AssignedQueueProperty);
        var assignedTo = ReadProperty(AssignedToProperty);
        var priority = ReadProperty(PriorityProperty);
        var title = ReadProperty(TitleProperty);

        if (oldData.Status != status.ToDbValue())
        {
            historyDal.Insert(new TicketHistoryDto(
                ticketId, "Status", oldData.Status, status.ToDbValue(), changedBy, now, null));
        }

        if (oldData.AssignedQueue != assignedQueue?.ToDbValue())
        {
            historyDal.Insert(new TicketHistoryDto(
                ticketId, "AssignedQueue", oldData.AssignedQueue, assignedQueue?.ToDbValue(), changedBy, now, null));
        }

        if (oldData.AssignedTo != assignedTo)
        {
            historyDal.Insert(new TicketHistoryDto(
                ticketId, "AssignedTo", oldData.AssignedTo, assignedTo, changedBy, now, null));
        }

        if (oldData.Priority != priority.ToDbValue())
        {
            historyDal.Insert(new TicketHistoryDto(
                ticketId, "Priority", oldData.Priority, priority.ToDbValue(), changedBy, now, null));
        }

        if (oldData.Title != title)
        {
            historyDal.Insert(new TicketHistoryDto(
                ticketId, "Title", oldData.Title, title, changedBy, now, null));
        }
    }

    #endregion

    #region Business Methods

    /// <summary>
    /// Closes the ticket with resolution notes.
    /// </summary>
    public void Close(string resolutionNotes)
    {
        ResolutionNotes = resolutionNotes;
        Status = TicketStatus.Closed;
        LoadProperty(ClosedAtProperty, DateTime.UtcNow);
    }

    /// <summary>
    /// Approves a purchase request.
    /// </summary>
    public void Approve()
    {
        if (Status != TicketStatus.PendingApproval)
        {
            throw new InvalidOperationException("Can only approve tickets that are pending approval.");
        }
        Status = TicketStatus.Approved;
    }

    /// <summary>
    /// Rejects a purchase request.
    /// </summary>
    public void Reject(string reason)
    {
        if (Status != TicketStatus.PendingApproval)
        {
            throw new InvalidOperationException("Can only reject tickets that are pending approval.");
        }
        Status = TicketStatus.Rejected;
        ResolutionNotes = $"Rejected: {reason}";
    }

    /// <summary>
    /// Assigns the ticket to a queue.
    /// </summary>
    public void AssignToQueue(TicketQueue queue, string? assignedTo = null)
    {
        AssignedQueue = queue;
        AssignedTo = assignedTo;
        if (Status == TicketStatus.New)
        {
            Status = TicketStatus.Triaged;
        }
    }

    #endregion
}

/// <summary>
/// Exception thrown when data is not found.
/// </summary>
public class DataNotFoundException : Exception
{
    public DataNotFoundException(string message) : base(message) { }
}
