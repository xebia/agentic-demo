using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ticketing.Chatbot.Services;

/// <summary>
/// Represents information about a tool that was called during chat processing.
/// </summary>
public record ToolCallInfo(string ToolName, string Arguments, string Result);

/// <summary>
/// Represents the result of a chat message, including any tool calls that were made.
/// </summary>
public record ChatResult(string Response, IReadOnlyList<ToolCallInfo> ToolCalls);

/// <summary>
/// Service for managing chat conversations with Azure OpenAI.
/// Maintains conversation history and handles message sending/receiving.
/// Supports automatic function/tool calling via MCP tools.
/// </summary>
public class ChatService
{
    private readonly IChatClient _chatClient;
    private readonly McpToolProvider _toolProvider;
    private readonly ILogger<ChatService> _logger;
    private readonly List<ChatMessage> _conversationHistory = [];
    private IList<AIFunction>? _tools;

    private const string SystemPrompt = """
        You are a helpful ticketing system assistant. You help users:
        - Create and manage support tickets
        - Check ticket status  
        - Get help with common issues
        - Escalate to human support when needed

        You have access to tools that can interact with the ticketing system.
        When users ask to list tickets, create tickets, or perform other ticketing operations,
        USE THE AVAILABLE TOOLS to fulfill their requests.

        Be concise, professional, and helpful. When you use a tool, summarize the results
        in a user-friendly way.
        """;

    public ChatService(IChatClient chatClient, McpToolProvider toolProvider, ILogger<ChatService> logger)
    {
        _chatClient = chatClient;
        _toolProvider = toolProvider;
        _logger = logger;
        // Initialize with system prompt
        _conversationHistory.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        _logger.LogInformation("ChatService initialized with system prompt and tool support");
    }

    /// <summary>
    /// Initializes the tools from MCP. Should be called after user authentication.
    /// </summary>
    public async Task InitializeToolsAsync()
    {
        try
        {
            _tools = await _toolProvider.GetToolsAsync();
            _logger.LogInformation("Initialized {ToolCount} tools for chat", _tools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize tools, continuing without tools");
            _tools = [];
        }
    }

    /// <summary>
    /// Sends a message to the chat and gets a response.
    /// Automatically invokes tools when the AI requests them.
    /// </summary>
    public async Task<ChatResult> SendMessageAsync(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            _logger.LogWarning("SendMessageAsync called with empty message");
            throw new ArgumentException("Message cannot be empty", nameof(userMessage));
        }

        _logger.LogInformation("Sending message to chat service: {MessageLength} chars", userMessage.Length);

        // Add user message to history
        _conversationHistory.Add(new ChatMessage(ChatRole.User, userMessage));

        var toolCalls = new List<ToolCallInfo>();

        try
        {
            // Initialize tools if not already done
            if (_tools == null)
            {
                await InitializeToolsAsync();
            }

            // Configure chat options with tools and auto-invocation
            var options = new ChatOptions();

            if (_tools?.Count > 0)
            {
                _logger.LogDebug("Adding {ToolCount} tools to chat options", _tools.Count);
                foreach (var tool in _tools)
                {
                    options.Tools ??= [];
                    options.Tools.Add(tool);
                }
                options.ToolMode = ChatToolMode.Auto;
            }

            _logger.LogDebug("Calling IChatClient.GetResponseAsync with {MessageCount} messages", _conversationHistory.Count);

            // Get response - the FunctionInvokingChatClient will handle tool calls automatically
            var response = await _chatClient.GetResponseAsync(_conversationHistory, options);

            _logger.LogDebug("Received response from chat service");

            // Check for tool calls in the response messages
            foreach (var msg in response.Messages)
            {
                foreach (var content in msg.Contents)
                {
                    if (content is FunctionCallContent functionCall)
                    {
                        _logger.LogInformation("Tool called: {ToolName}", functionCall.Name);
                        var argsJson = functionCall.Arguments != null 
                            ? System.Text.Json.JsonSerializer.Serialize(functionCall.Arguments)
                            : "{}";
                        toolCalls.Add(new ToolCallInfo(functionCall.Name, argsJson, ""));
                    }
                    else if (content is FunctionResultContent functionResult)
                    {
                        _logger.LogInformation("Tool result received for call {CallId}", functionResult.CallId);

                        string resultStr;
                        if (functionResult.Exception != null)
                        {
                            _logger.LogError(functionResult.Exception, 
                                "Tool execution failed with exception for call {CallId}", functionResult.CallId);
                            resultStr = $"Error: {functionResult.Exception.Message}";
                        }
                        else
                        {
                            resultStr = functionResult.Result?.ToString() ?? "";
                        }

                        // Update the corresponding tool call with the result
                        if (toolCalls.Count > 0)
                        {
                            var lastCall = toolCalls[^1];
                            toolCalls[^1] = lastCall with { Result = resultStr };
                        }
                    }
                }
            }

            // Extract text from response
            var assistantMessage = response.Text ?? "I apologize, but I couldn't generate a response.";

            _logger.LogInformation("Chat response received: {ResponseLength} chars, {ToolCallCount} tool calls", 
                assistantMessage.Length, toolCalls.Count);

            // Add response messages to history (includes tool interactions)
            foreach (var msg in response.Messages)
            {
                _conversationHistory.Add(msg);
            }

            return new ChatResult(assistantMessage, toolCalls);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get response from chat service. Exception type: {ExceptionType}, Message: {ExceptionMessage}", 
                ex.GetType().FullName, ex.Message);

            // Log inner exception if present
            if (ex.InnerException != null)
            {
                _logger.LogError("Inner exception: {InnerExceptionType}, Message: {InnerExceptionMessage}", 
                    ex.InnerException.GetType().FullName, ex.InnerException.Message);
            }

            // Remove the user message if we couldn't get a response
            _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
            throw new InvalidOperationException("Failed to get response from chat service", ex);
        }
    }

    /// <summary>
    /// Clears the conversation history (keeps system prompt).
    /// </summary>
    public void ClearHistory()
    {
        _logger.LogInformation("Clearing conversation history");
        _conversationHistory.Clear();
        _conversationHistory.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        _tools = null; // Force re-initialization of tools
        _toolProvider.ClearCache();
    }

    /// <summary>
    /// Gets the current conversation history.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetHistory() => _conversationHistory.AsReadOnly();
}
