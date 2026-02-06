namespace Ticketing.Chatbot.Models;

/// <summary>
/// Represents a message in the chat conversation.
/// </summary>
public class ChatMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsToolCall { get; set; }
    public string? ToolName { get; set; }
    public string? ToolResult { get; set; }
}

public static class ChatRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string System = "system";
    public const string Tool = "tool";
}
