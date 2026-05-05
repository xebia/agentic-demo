using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Chat;
using Ticketing.Chatbot.Models;

namespace Ticketing.Chatbot.Services;

public record ToolCallInfo(string ToolName, string Arguments, string Result);

public record ChatResult(string Response, IReadOnlyList<ToolCallInfo> ToolCalls);

/// <summary>
/// Per-circuit Microsoft Agent Framework concierge: owns an MCP client (lazily
/// initialized after the user authenticates), an AIAgent backed by Azure OpenAI
/// with the MCP tools registered, and an AgentSession that carries conversation
/// state across turns.
/// </summary>
public class ChatService : IAsyncDisposable
{
    private readonly ChatClient _chatClient;
    private readonly UserSessionService _userSession;
    private readonly ChatSettings _settings;
    private readonly ILogger<ChatService> _logger;
    private readonly ILoggerFactory _loggerFactory;

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

    private HttpClient? _mcpHttpClient;
    private McpClient? _mcpClient;
    private AIAgent? _agent;
    private AgentSession? _session;
    private IList<McpClientTool>? _tools;

    public ChatService(
        ChatClient chatClient,
        UserSessionService userSession,
        IOptions<ChatSettings> settings,
        ILogger<ChatService> logger,
        ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _userSession = userSession;
        _settings = settings.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Sends a user message through the agent and returns the response plus any
    /// tool calls that occurred during the turn.
    /// </summary>
    public async Task<ChatResult> SendMessageAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("Message cannot be empty", nameof(userMessage));
        }

        await EnsureAgentAsync(cancellationToken);

        _logger.LogInformation("Running agent for {Length}-char user message", userMessage.Length);

        AgentResponse response = await _agent!.RunAsync(userMessage, _session!, cancellationToken: cancellationToken);

        var toolCalls = ExtractToolCalls(response);
        var text = response.Text ?? "I apologize, but I couldn't generate a response.";

        _logger.LogInformation("Agent returned {Length} chars, {ToolCount} tool calls", text.Length, toolCalls.Count);
        return new ChatResult(text, toolCalls);
    }

    /// <summary>
    /// Lists tools advertised by the MCP server using the current user's auth.
    /// </summary>
    public async Task<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMcpClientAsync(cancellationToken);
        return _tools ?? [];
    }

    /// <summary>
    /// Invokes an MCP tool directly (used by the Tools explorer page).
    /// </summary>
    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken = default)
    {
        await EnsureMcpClientAsync(cancellationToken);

        var result = await _mcpClient!.CallToolAsync(
            toolName,
            arguments ?? new Dictionary<string, object?>(),
            cancellationToken: cancellationToken);

        var blocks = result.Content.OfType<TextContentBlock>().Select(t => t.Text);
        return string.Join("\n", blocks);
    }

    /// <summary>
    /// Drops conversation state and forces re-init on next use (e.g., user switch).
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        _logger.LogInformation("Clearing conversation history and resetting agent");
        _session = null;
        _agent = null;
        _tools = null;
        if (_mcpClient != null)
        {
            await _mcpClient.DisposeAsync();
            _mcpClient = null;
        }
        _mcpHttpClient?.Dispose();
        _mcpHttpClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ClearHistoryAsync();
        GC.SuppressFinalize(this);
    }

    private async Task EnsureAgentAsync(CancellationToken cancellationToken)
    {
        await EnsureMcpClientAsync(cancellationToken);

        if (_agent == null)
        {
            _logger.LogInformation("Building AIAgent with {ToolCount} MCP tools", _tools!.Count);
            _agent = _chatClient.AsAIAgent(
                instructions: SystemPrompt,
                tools: [.. _tools!.Cast<AITool>()]);
        }

        _session ??= await _agent.CreateSessionAsync(cancellationToken: cancellationToken);
    }

    private async Task EnsureMcpClientAsync(CancellationToken cancellationToken)
    {
        if (!_userSession.IsAuthenticated)
        {
            throw new InvalidOperationException("User is not authenticated; cannot connect to MCP server.");
        }

        if (_mcpClient != null)
        {
            return;
        }

        _logger.LogInformation("Connecting MCP client to {Url}", _settings.McpEndpointUrl);

        _mcpHttpClient = new HttpClient(new BearerTokenHandler(
            _userSession,
            _loggerFactory.CreateLogger<BearerTokenHandler>())
        {
            InnerHandler = new HttpClientHandler()
        });

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(_settings.McpEndpointUrl),
                Name = "Ticketing"
            },
            _mcpHttpClient,
            _loggerFactory);

        _mcpClient = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);

        _tools = await _mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        _logger.LogInformation("Loaded {Count} MCP tools", _tools.Count);
    }

    private static List<ToolCallInfo> ExtractToolCalls(AgentResponse response)
    {
        var calls = new Dictionary<string, ToolCallInfo>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc)
                {
                    var args = fc.Arguments != null
                        ? System.Text.Json.JsonSerializer.Serialize(fc.Arguments)
                        : "{}";
                    calls[fc.CallId] = new ToolCallInfo(fc.Name, args, "");
                    order.Add(fc.CallId);
                }
                else if (content is FunctionResultContent fr && calls.TryGetValue(fr.CallId, out var existing))
                {
                    var resultStr = fr.Exception != null
                        ? $"Error: {fr.Exception.Message}"
                        : fr.Result?.ToString() ?? "";
                    calls[fr.CallId] = existing with { Result = resultStr };
                }
            }
        }

        return order.Select(id => calls[id]).ToList();
    }
}
