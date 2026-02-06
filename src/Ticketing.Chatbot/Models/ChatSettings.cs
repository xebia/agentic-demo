namespace Ticketing.Chatbot.Models;

/// <summary>
/// Configuration settings for the chatbot.
/// </summary>
public class ChatSettings
{
    /// <summary>
    /// URL of the auth service.
    /// </summary>
    public string AuthServiceUrl { get; set; } = "http://localhost:5001";

    /// <summary>
    /// URL of the ticketing service MCP endpoint.
    /// </summary>
    public string McpEndpointUrl { get; set; } = "http://localhost:5000/mcp";
}
