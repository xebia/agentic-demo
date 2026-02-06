namespace Ticketing.TriageAgent.Models;

/// <summary>
/// Response model for paginated ticket list from the REST API.
/// </summary>
public class TicketListResponse
{
    public List<TicketListItemResponse> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Summary model for a ticket in a list response.
/// </summary>
public class TicketListItemResponse
{
    public required string TicketId { get; set; }
    public required string Title { get; set; }
    public required string TicketType { get; set; }
    public string? Category { get; set; }
    public required string Priority { get; set; }
    public required string Status { get; set; }
    public string? AssignedQueue { get; set; }
    public string? AssignedTo { get; set; }
    public required string CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ParentTicketId { get; set; }
}

/// <summary>
/// Detailed model for a single ticket from the REST API.
/// </summary>
public class TicketDetailResponse
{
    public required string TicketId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string TicketType { get; set; }
    public string? Category { get; set; }
    public required string Priority { get; set; }
    public required string Status { get; set; }
    public string? AssignedQueue { get; set; }
    public string? AssignedTo { get; set; }
    public required string CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? TriageDecision { get; set; }
    public string? TriageNotes { get; set; }
    public string? ResolutionNotes { get; set; }
    public string? ParentTicketId { get; set; }
}

/// <summary>
/// Request model for updating a ticket via the REST API.
/// </summary>
public class UpdateTicketRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? TicketType { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? Status { get; set; }
    public string? AssignedQueue { get; set; }
    public string? AssignedTo { get; set; }
    public string? TriageDecision { get; set; }
    public string? TriageNotes { get; set; }
    public string? ResolutionNotes { get; set; }
    public string? ParentTicketId { get; set; }
}

/// <summary>
/// Token response from the auth service client credentials endpoint.
/// </summary>
public class AuthTokenResponse
{
    public required string Token { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public DateTime ExpiresAt { get; set; }
}
