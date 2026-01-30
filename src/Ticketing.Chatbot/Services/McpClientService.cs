using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ticketing.Chatbot.Models;

namespace Ticketing.Chatbot.Services;

/// <summary>
/// Service for interacting with the MCP endpoint.
/// This is a simple HTTP-based MCP client for the chatbot.
/// </summary>
public class McpClientService
{
    private readonly HttpClient _httpClient;
    private readonly ChatSettings _settings;
    private readonly UserSessionService _userSession;
    private readonly ILogger<McpClientService> _logger;

    public McpClientService(
        HttpClient httpClient,
        IOptions<ChatSettings> settings,
        UserSessionService userSession,
        ILogger<McpClientService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _userSession = userSession;
        _logger = logger;
    }

    /// <summary>
    /// Lists available tools from the MCP endpoint.
    /// </summary>
    public async Task<JsonDocument?> ListToolsAsync()
    {
        if (!_userSession.IsAuthenticated)
        {
            throw new InvalidOperationException("User not authenticated");
        }

        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list"
        };

        return await SendMcpRequestAsync(request);
    }

    /// <summary>
    /// Calls an MCP tool with the specified arguments.
    /// </summary>
    public async Task<JsonDocument?> CallToolAsync(string toolName, Dictionary<string, object>? arguments = null)
    {
        if (!_userSession.IsAuthenticated)
        {
            throw new InvalidOperationException("User not authenticated");
        }

        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments ?? new Dictionary<string, object>()
            }
        };

        return await SendMcpRequestAsync(request);
    }

    private async Task<JsonDocument?> SendMcpRequestAsync(object request)
    {
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.McpEndpointUrl);
        httpRequest.Content = content;
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _userSession.AccessToken);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        _logger.LogDebug("Sending MCP request: {Request}", json);

        var response = await _httpClient.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("MCP response: {Response}", responseBody);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("MCP request failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
            return null;
        }

        // Check if response is SSE format (text/event-stream)
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType == "text/event-stream")
        {
            // Parse SSE format - extract JSON from "data:" lines
            var jsonData = ParseSseResponse(responseBody);
            if (jsonData == null)
            {
                _logger.LogError("Failed to parse SSE response");
                return null;
            }
            return JsonDocument.Parse(jsonData);
        }

        return JsonDocument.Parse(responseBody);
    }

    private string? ParseSseResponse(string sseResponse)
    {
        // SSE format: lines starting with "data:" contain the JSON payload
        // Multiple data lines may need to be concatenated
        var dataLines = new List<string>();

        foreach (var line in sseResponse.Split('\n'))
        {
            if (line.StartsWith("data:"))
            {
                var data = line.Substring(5).Trim();
                if (!string.IsNullOrEmpty(data))
                {
                    dataLines.Add(data);
                }
            }
        }

        if (dataLines.Count == 0)
        {
            return null;
        }

        // Return the last data line (which should contain the final result)
        return dataLines[^1];
    }
}
