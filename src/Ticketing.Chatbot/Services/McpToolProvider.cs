using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Ticketing.Chatbot.Services;

/// <summary>
/// Provides MCP tools as AIFunctions for use with Microsoft.Extensions.AI chat clients.
/// This bridges the gap between MCP tool definitions and the AI function calling interface.
/// </summary>
public class McpToolProvider
{
    private readonly McpClientService _mcpClient;
    private readonly ILogger<McpToolProvider> _logger;
    private List<AIFunction>? _cachedTools;

    public McpToolProvider(McpClientService mcpClient, ILogger<McpToolProvider> logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the MCP tools as AIFunctions that can be used with the chat client.
    /// </summary>
    public async Task<IList<AIFunction>> GetToolsAsync()
    {
        if (_cachedTools != null)
            return _cachedTools;

        _logger.LogInformation("Fetching MCP tools...");

        var toolsResponse = await _mcpClient.ListToolsAsync();
        if (toolsResponse == null)
        {
            _logger.LogWarning("Failed to fetch MCP tools, returning empty list");
            return [];
        }

        var tools = new List<AIFunction>();

        // Parse the MCP tools/list response
        // Expected format: { "result": { "tools": [ { "name": "...", "description": "...", "inputSchema": {...} } ] } }
        if (toolsResponse.RootElement.TryGetProperty("result", out var result) &&
            result.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var tool in toolsArray.EnumerateArray())
            {
                var name = tool.GetProperty("name").GetString() ?? "unknown";
                var description = tool.TryGetProperty("description", out var desc) 
                    ? desc.GetString() ?? "" 
                    : "";

                _logger.LogDebug("Registering MCP tool: {ToolName} - {Description}", name, description);

                // Create an AIFunction that wraps the MCP tool call
                var aiFunction = CreateMcpToolFunction(name, description, tool);
                tools.Add(aiFunction);
            }
        }

        _logger.LogInformation("Loaded {Count} MCP tools", tools.Count);
        _cachedTools = tools;
        return tools;
    }

    /// <summary>
    /// Clears the cached tools, forcing a refresh on next access.
    /// </summary>
    public void ClearCache()
    {
        _cachedTools = null;
    }

    private AIFunction CreateMcpToolFunction(string name, string description, JsonElement toolDefinition)
    {
        // Capture the McpClientService and logger for use in the delegate
        var mcpClient = _mcpClient;
        var logger = _logger;

        // Extract the inputSchema from the tool definition
        JsonElement? inputSchema = toolDefinition.TryGetProperty("inputSchema", out var schema) ? schema : null;

        // Log the parameters for debugging
        if (inputSchema.HasValue)
        {
            LogToolParameters(name, inputSchema.Value);
        }

        // Build an enhanced description that includes parameter requirements
        var enhancedDescription = BuildEnhancedDescription(description, inputSchema);

        // Create the core invocation function
        async Task<object?> InvokeCore(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            try
            {
                logger.LogInformation("MCP tool invoked: {ToolName}", name);

                var argDict = new Dictionary<string, object>();

                foreach (var kvp in arguments)
                {
                    if (kvp.Value != null)
                    {
                        // Handle different value types from the AI
                        var value = kvp.Value switch
                        {
                            JsonElement je => je.ValueKind switch
                            {
                                JsonValueKind.String => je.GetString() ?? "",
                                JsonValueKind.Number => je.TryGetInt32(out var intVal) ? intVal : je.GetDouble(),
                                JsonValueKind.True => (object)true,
                                JsonValueKind.False => (object)false,
                                _ => je.GetRawText()
                            },
                            JsonNode jn => jn.GetValueKind() switch
                            {
                                JsonValueKind.String => jn.GetValue<string>(),
                                JsonValueKind.Number => jn.GetValue<double>(),
                                JsonValueKind.True => (object)true,
                                JsonValueKind.False => (object)false,
                                _ => jn.ToJsonString()
                            },
                            _ => kvp.Value
                        };
                        argDict[kvp.Key] = value;
                        logger.LogDebug("Tool {ToolName} arg: {Key} = {Value} ({Type})", 
                            name, kvp.Key, value, value?.GetType().Name ?? "null");
                    }
                }

                logger.LogInformation("MCP tool {ToolName} invoking with {ArgCount} arguments: {Args}", 
                    name, argDict.Count, string.Join(", ", argDict.Keys));

                return await InvokeMcpToolAsync(mcpClient, logger, name, argDict);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in MCP tool {ToolName}: {Message}", name, ex.Message);
                return $"Error executing {name}: {ex.Message}";
            }
        }

        // Parse JSON schema from the MCP inputSchema for proper parameter metadata
        var jsonSchema = inputSchema ?? JsonDocument.Parse("{}").RootElement;

        // Return a custom AIFunction that uses our InvokeCore with proper schema
        return new DelegatingAIFunction(name, enhancedDescription, InvokeCore, jsonSchema);
    }

    /// <summary>
    /// Custom AIFunction that delegates to a specific invoke implementation
    /// while maintaining proper metadata from the JSON schema.
    /// </summary>
    private sealed class DelegatingAIFunction : AIFunction
    {
        private readonly Func<AIFunctionArguments, CancellationToken, Task<object?>> _invokeFunc;
        private readonly JsonElement _jsonSchema;

        public DelegatingAIFunction(
            string name, 
            string description,
            Func<AIFunctionArguments, CancellationToken, Task<object?>> invokeFunc,
            JsonElement jsonSchema)
        {
            _invokeFunc = invokeFunc;
            _jsonSchema = jsonSchema;
            Name = name;
            Description = description;
        }

        public override string Name { get; }
        public override string Description { get; }
        public override JsonElement JsonSchema => _jsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, 
            CancellationToken cancellationToken)
        {
            return await _invokeFunc(arguments, cancellationToken);
        }
    }

    private static string BuildEnhancedDescription(string description, JsonElement? inputSchema)
    {
        if (inputSchema == null)
            return description;

        var sb = new System.Text.StringBuilder();
        sb.Append(description);

        var schema = inputSchema.Value;
        var requiredParams = new HashSet<string>();

        // Get required fields array
        if (schema.TryGetProperty("required", out var requiredArray))
        {
            foreach (var req in requiredArray.EnumerateArray())
            {
                var reqName = req.GetString();
                if (reqName != null)
                    requiredParams.Add(reqName);
            }
        }

        // Parse properties and add to description
        if (schema.TryGetProperty("properties", out var properties) && properties.EnumerateObject().Any())
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Required arguments:");

            foreach (var prop in properties.EnumerateObject())
            {
                var paramName = prop.Name;
                var paramSchema = prop.Value;

                var paramDescription = paramSchema.TryGetProperty("description", out var descProp)
                    ? descProp.GetString() ?? ""
                    : "";

                var paramType = paramSchema.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString() ?? "string"
                    : "string";

                var isRequired = requiredParams.Contains(paramName);
                var requiredMarker = isRequired ? " [REQUIRED]" : "";

                sb.AppendLine($"- {paramName} ({paramType}){requiredMarker}: {paramDescription}");
            }
        }

        return sb.ToString();
    }

    private void LogToolParameters(string toolName, JsonElement inputSchema)
    {
        var requiredParams = new HashSet<string>();

        if (inputSchema.TryGetProperty("required", out var requiredArray))
        {
            foreach (var req in requiredArray.EnumerateArray())
            {
                var reqName = req.GetString();
                if (reqName != null)
                    requiredParams.Add(reqName);
            }
        }

        if (inputSchema.TryGetProperty("properties", out var properties))
        {
            foreach (var prop in properties.EnumerateObject())
            {
                var paramName = prop.Name;
                var paramSchema = prop.Value;

                var paramType = paramSchema.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString() ?? "string"
                    : "string";

                var isRequired = requiredParams.Contains(paramName);

                _logger.LogDebug("Tool {ToolName} schema param: {ParamName} ({Type}, required={Required})",
                    toolName, paramName, paramType, isRequired);
            }
        }
    }

    private static async Task<string> InvokeMcpToolAsync(
        McpClientService mcpClient, 
        ILogger logger, 
        string toolName, 
        Dictionary<string, object> arguments)
    {
        logger.LogInformation("Invoking MCP tool: {ToolName} with {ArgCount} arguments", toolName, arguments.Count);

        try
        {
            var result = await mcpClient.CallToolAsync(toolName, arguments);

            if (result == null)
            {
                logger.LogWarning("MCP tool {ToolName} returned null", toolName);
                return "Tool execution failed - no response received. Check if the MCP server is running and accessible.";
            }

            // Log the raw response for debugging
            logger.LogDebug("MCP tool {ToolName} raw response: {Response}", toolName, result.RootElement.GetRawText());

            // Check for JSON-RPC error
            if (result.RootElement.TryGetProperty("error", out var error))
            {
                var errorMessage = error.TryGetProperty("message", out var msg) 
                    ? msg.GetString() 
                    : error.GetRawText();
                logger.LogError("MCP tool {ToolName} returned error: {Error}", toolName, errorMessage);
                return $"Tool error: {errorMessage}";
            }

            // Extract the result content
            // Expected format: { "result": { "content": [...] } }
            if (result.RootElement.TryGetProperty("result", out var toolResult))
            {
                if (toolResult.TryGetProperty("content", out var content))
                {
                    // Content is an array of content items
                    var textParts = new List<string>();
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.TryGetProperty("text", out var text))
                        {
                            textParts.Add(text.GetString() ?? "");
                        }
                    }
                    var resultText = string.Join("\n", textParts);
                    logger.LogInformation("MCP tool {ToolName} succeeded with {Length} chars result", toolName, resultText.Length);
                    return resultText;
                }
                return toolResult.GetRawText();
            }

            return result.RootElement.GetRawText();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not authenticated"))
        {
            logger.LogError(ex, "Authentication error invoking MCP tool {ToolName}", toolName);
            return "Error: User is not authenticated. Please log in again.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking MCP tool {ToolName}", toolName);
            return $"Error executing tool: {ex.Message}";
        }
    }
}
