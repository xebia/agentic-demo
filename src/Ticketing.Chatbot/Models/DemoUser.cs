namespace Ticketing.Chatbot.Models;

/// <summary>
/// Represents a demo user that can be selected for chat.
/// </summary>
public class DemoUser
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string[] Roles { get; set; }
}

/// <summary>
/// Response from the auth service token endpoint.
/// </summary>
public class TokenResponse
{
    public required string Token { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public DateTime ExpiresAt { get; set; }
    public required TokenSubjectInfo Subject { get; set; }
}

public class TokenSubjectInfo
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string[] Roles { get; set; }
    public bool IsServiceAccount { get; set; }
}
