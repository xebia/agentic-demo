namespace Ticketing.Domain;

/// <summary>
/// Ticket status values.
/// </summary>
public enum TicketStatus
{
    New,
    Triaged,
    InProgress,
    PendingApproval,
    Approved,
    Rejected,
    PendingFulfillment,
    Fulfilled,
    Resolved,
    Closed
}

/// <summary>
/// Ticket priority levels.
/// </summary>
public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Types of tickets in the system.
/// </summary>
public enum TicketType
{
    Support,
    Purchase,
    Delivery
}

/// <summary>
/// Queue assignments for tickets.
/// </summary>
public enum TicketQueue
{
    Helpdesk,
    Purchasing,
    Fulfillment
}

/// <summary>
/// Category classifications for tickets.
/// </summary>
public enum TicketCategory
{
    Hardware,
    Software,
    Access,
    Network,
    Other
}

/// <summary>
/// Extension methods for ticket enums.
/// </summary>
public static class TicketEnumExtensions
{
    public static string ToDbValue(this TicketStatus status) => status switch
    {
        TicketStatus.New => "new",
        TicketStatus.Triaged => "triaged",
        TicketStatus.InProgress => "in-progress",
        TicketStatus.PendingApproval => "pending-approval",
        TicketStatus.Approved => "approved",
        TicketStatus.Rejected => "rejected",
        TicketStatus.PendingFulfillment => "pending-fulfillment",
        TicketStatus.Fulfilled => "fulfilled",
        TicketStatus.Resolved => "resolved",
        TicketStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static TicketStatus ToTicketStatus(this string value) => value switch
    {
        "new" => TicketStatus.New,
        "triaged" => TicketStatus.Triaged,
        "in-progress" => TicketStatus.InProgress,
        "pending-approval" => TicketStatus.PendingApproval,
        "approved" => TicketStatus.Approved,
        "rejected" => TicketStatus.Rejected,
        "pending-fulfillment" => TicketStatus.PendingFulfillment,
        "fulfilled" => TicketStatus.Fulfilled,
        "resolved" => TicketStatus.Resolved,
        "closed" => TicketStatus.Closed,
        _ => TicketStatus.New
    };

    public static string ToDbValue(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "low",
        TicketPriority.Medium => "medium",
        TicketPriority.High => "high",
        TicketPriority.Critical => "critical",
        _ => "medium"
    };

    public static TicketPriority ToTicketPriority(this string value) => value switch
    {
        "low" => TicketPriority.Low,
        "medium" => TicketPriority.Medium,
        "high" => TicketPriority.High,
        "critical" => TicketPriority.Critical,
        _ => TicketPriority.Medium
    };

    public static string ToDbValue(this TicketType type) => type switch
    {
        TicketType.Support => "support",
        TicketType.Purchase => "purchase",
        TicketType.Delivery => "delivery",
        _ => "support"
    };

    public static TicketType ToTicketType(this string value) => value switch
    {
        "support" => TicketType.Support,
        "purchase" => TicketType.Purchase,
        "delivery" => TicketType.Delivery,
        _ => TicketType.Support
    };

    public static string ToDbValue(this TicketQueue queue) => queue switch
    {
        TicketQueue.Helpdesk => "helpdesk",
        TicketQueue.Purchasing => "purchasing",
        TicketQueue.Fulfillment => "fulfillment",
        _ => "helpdesk"
    };

    public static TicketQueue? ToTicketQueue(this string? value) => value switch
    {
        "helpdesk" => TicketQueue.Helpdesk,
        "purchasing" => TicketQueue.Purchasing,
        "fulfillment" => TicketQueue.Fulfillment,
        _ => null
    };

    public static string ToDbValue(this TicketCategory category) => category switch
    {
        TicketCategory.Hardware => "hardware",
        TicketCategory.Software => "software",
        TicketCategory.Access => "access",
        TicketCategory.Network => "network",
        TicketCategory.Other => "other",
        _ => "other"
    };

    public static TicketCategory? ToTicketCategory(this string? value) => value switch
    {
        "hardware" => TicketCategory.Hardware,
        "software" => TicketCategory.Software,
        "access" => TicketCategory.Access,
        "network" => TicketCategory.Network,
        "other" => TicketCategory.Other,
        _ => null
    };

    public static string GetDisplayName(this TicketStatus status) => status switch
    {
        TicketStatus.New => "New",
        TicketStatus.Triaged => "Triaged",
        TicketStatus.InProgress => "In Progress",
        TicketStatus.PendingApproval => "Pending Approval",
        TicketStatus.Approved => "Approved",
        TicketStatus.Rejected => "Rejected",
        TicketStatus.PendingFulfillment => "Pending Fulfillment",
        TicketStatus.Fulfilled => "Fulfilled",
        TicketStatus.Resolved => "Resolved",
        TicketStatus.Closed => "Closed",
        _ => status.ToString()
    };

    public static string GetDisplayName(this TicketQueue queue) => queue switch
    {
        TicketQueue.Helpdesk => "Help Desk",
        TicketQueue.Purchasing => "Purchasing",
        TicketQueue.Fulfillment => "Fulfillment",
        _ => queue.ToString()
    };
}
