using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Ticketing.Messaging.Abstractions.Diagnostics;
using Ticketing.OperationsAgent.Models;

namespace Ticketing.OperationsAgent.Services;

/// <summary>
/// Uses Azure OpenAI to analyze health scan results and produce operations alerts.
/// </summary>
public class AzureOpenAIOperationsAnalyzer : IOperationsAnalyzer
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAIOperationsAnalyzer> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SystemPrompt = """
        You are an IT operations monitoring agent. You analyze system health data and produce
        actionable alerts for the operations team.

        You will receive a health scan result containing:
        - Service health statuses (up/down, response times)
        - Dead letter queue depths (messages that failed processing)
        - SLA violations (tickets that have been waiting too long)

        For each issue found, produce an alert with:
        - severity: "Critical" (service down, major SLA breach), "Warning" (degraded, approaching limits), or "Info" (notable but not urgent)
        - title: Short, descriptive title
        - description: What is happening and what the impact is
        - remediation: Suggested action to resolve the issue

        Guidelines:
        - A service being down is always Critical
        - DLQ messages > 10 is Critical, > 0 is Warning
        - SLA violations for Critical priority tickets are Critical alerts
        - SLA violations for other priorities are Warning alerts
        - Slow response times (> 2000ms) are Warning
        - Combine related issues into a single alert when appropriate
        - Be concise but specific — include ticket IDs and service names
        """;

    public AzureOpenAIOperationsAnalyzer(
        IConfiguration configuration,
        ILogger<AzureOpenAIOperationsAnalyzer> logger)
    {
        _logger = logger;

        var endpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured");
        var apiKey = configuration["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is not configured");
        var deploymentName = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

        var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _chatClient = client.GetChatClient(deploymentName);
    }

    public async Task<List<OperationsAlert>> AnalyzeHealthScanAsync(
        HealthScanResult scanResult, CancellationToken ct = default)
    {
        _logger.LogInformation("Analyzing health scan with LLM");

        using var activity = TicketingTelemetry.Source.StartActivity("openai.chat.completion");
        activity?.SetTag("agent", "operations");
        var sw = Stopwatch.StartNew();

        try
        {
            var userMessage = JsonSerializer.Serialize(scanResult, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var options = new ChatCompletionOptions
            {
                Temperature = 0.1f,
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "operations_alerts",
                    BinaryData.FromString("""
                        {
                            "type": "object",
                            "properties": {
                                "alerts": {
                                    "type": "array",
                                    "items": {
                                        "type": "object",
                                        "properties": {
                                            "severity": {
                                                "type": "string",
                                                "enum": ["Critical", "Warning", "Info"]
                                            },
                                            "title": {
                                                "type": "string"
                                            },
                                            "description": {
                                                "type": "string"
                                            },
                                            "remediation": {
                                                "type": "string"
                                            }
                                        },
                                        "required": ["severity", "title", "description", "remediation"],
                                        "additionalProperties": false
                                    }
                                }
                            },
                            "required": ["alerts"],
                            "additionalProperties": false
                        }
                        """),
                    jsonSchemaIsStrict: true)
            };

            var completion = await _chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(SystemPrompt),
                    new UserChatMessage(userMessage)
                ],
                options,
                ct);

            sw.Stop();
            var usage = completion.Value.Usage;
            activity?.SetTag("llm.model", completion.Value.Model);
            activity?.SetTag("llm.input_tokens", usage.InputTokenCount);
            activity?.SetTag("llm.output_tokens", usage.OutputTokenCount);

            TicketingTelemetry.LlmCalls.Add(1,
                new KeyValuePair<string, object?>("agent", "operations"),
                new KeyValuePair<string, object?>("status", "success"));
            TicketingTelemetry.LlmDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("agent", "operations"));
            TicketingTelemetry.LlmTokens.Add(usage.InputTokenCount,
                new KeyValuePair<string, object?>("agent", "operations"),
                new KeyValuePair<string, object?>("type", "input"));
            TicketingTelemetry.LlmTokens.Add(usage.OutputTokenCount,
                new KeyValuePair<string, object?>("agent", "operations"),
                new KeyValuePair<string, object?>("type", "output"));

            var responseText = completion.Value.Content[0].Text;
            _logger.LogDebug("LLM response: {Response}", responseText);

            var wrapper = JsonSerializer.Deserialize<AlertsWrapper>(responseText, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize alerts response");

            var alerts = wrapper.Alerts.Select(a => new OperationsAlert
            {
                Severity = a.Severity,
                Title = a.Title,
                Description = a.Description,
                Remediation = a.Remediation,
                Source = "HealthScan"
            }).ToList();

            _logger.LogInformation("LLM produced {Count} alerts", alerts.Count);
            return alerts;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            sw.Stop();
            TicketingTelemetry.LlmCalls.Add(1,
                new KeyValuePair<string, object?>("agent", "operations"),
                new KeyValuePair<string, object?>("status", "transient_error"));
            _logger.LogWarning(ex, "Transient LLM error during health scan analysis");
            throw;
        }
        catch (JsonException ex)
        {
            sw.Stop();
            TicketingTelemetry.LlmCalls.Add(1,
                new KeyValuePair<string, object?>("agent", "operations"),
                new KeyValuePair<string, object?>("status", "parse_error"));
            _logger.LogError(ex, "Failed to parse LLM response for health scan analysis");
            throw;
        }
    }

    private class AlertsWrapper
    {
        public List<AlertItem> Alerts { get; set; } = [];
    }

    private class AlertItem
    {
        public required string Severity { get; set; }
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required string Remediation { get; set; }
    }
}
