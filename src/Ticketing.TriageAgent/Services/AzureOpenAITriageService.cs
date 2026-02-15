using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Ticketing.Messaging.Abstractions.Diagnostics;
using Ticketing.TriageAgent.Models;

namespace Ticketing.TriageAgent.Services;

/// <summary>
/// Uses Azure OpenAI to analyze tickets and produce triage decisions.
/// Enforces structured JSON output for reliable parsing.
/// </summary>
public class AzureOpenAITriageService : ITriageService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAITriageService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SystemPrompt = """
        You are an IT support triage agent for an organization's internal ticketing system.
        Your job is to analyze incoming tickets and make routing decisions.

        ## Routing Rules

        **Helpdesk Queue** — Route here if the ticket is about:
        - Technical support issues (software bugs, crashes, errors)
        - Hardware problems (broken equipment, peripherals not working)
        - Network/connectivity issues
        - Account access issues (locked accounts, password resets, permission requests)
        - General IT support questions
        - Service requests for IT assistance

        **Purchasing Queue** — Route here if the ticket is about:
        - Requests to buy new equipment (laptops, monitors, keyboards, etc.)
        - Software license purchases or renewals
        - Office supply orders
        - Any request that involves spending money or procurement
        - Subscription renewals or new subscriptions

        ## Priority Guidelines

        - **Critical**: System-wide outage, security breach, complete inability to work for multiple users
        - **High**: Single user completely blocked from working, data loss risk, VIP/executive request
        - **Medium**: Inconvenient but workaround exists, standard requests, routine issues
        - **Low**: Nice-to-have, cosmetic issues, future planning, informational requests

        Maintain the original ticket priority unless the content clearly warrants a change.
        For example, if a "Medium" priority ticket describes a critical security breach, escalate to "Critical".

        ## Category Assignment

        Assign one of these categories based on the ticket content:
        - **Hardware**: Physical equipment, peripherals, devices
        - **Software**: Applications, operating systems, software bugs
        - **Network**: Connectivity, VPN, Wi-Fi, internet access
        - **Access**: Permissions, accounts, authentication, authorization
        - **Other**: Anything that doesn't fit the above categories

        ## Response Format

        Respond with a JSON object containing:
        - queue: "Helpdesk" or "Purchasing"
        - priority: "Low", "Medium", "High", or "Critical"
        - category: "Hardware", "Software", "Network", "Access", or "Other"
        - reasoning: Brief explanation (1-3 sentences) of why you made this routing decision
        """;

    public AzureOpenAITriageService(
        IConfiguration configuration,
        ILogger<AzureOpenAITriageService> logger)
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

    public async Task<TriageDecision> TriageTicketAsync(
        TicketDetailResponse ticket,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Triaging ticket {TicketId}: {Title}", ticket.TicketId, ticket.Title);

        using var activity = TicketingTelemetry.Source.StartActivity("openai.chat.completion");
        activity?.SetTag("ticket.id", ticket.TicketId);
        activity?.SetTag("agent", "triage");
        var sw = Stopwatch.StartNew();

        try
        {
            var userMessage = $"""
                Ticket ID: {ticket.TicketId}
                Type: {ticket.TicketType}
                Title: {ticket.Title}
                Description: {ticket.Description ?? "(no description)"}
                Current Priority: {ticket.Priority}
                Current Category: {ticket.Category ?? "(not set)"}
                Created By: {ticket.CreatedByName ?? ticket.CreatedBy}
                """;

            var options = new ChatCompletionOptions
            {
                Temperature = 0.1f,
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "triage_decision",
                    BinaryData.FromString("""
                        {
                            "type": "object",
                            "properties": {
                                "queue": {
                                    "type": "string",
                                    "enum": ["Helpdesk", "Purchasing"]
                                },
                                "priority": {
                                    "type": "string",
                                    "enum": ["Low", "Medium", "High", "Critical"]
                                },
                                "category": {
                                    "type": "string",
                                    "enum": ["Hardware", "Software", "Network", "Access", "Other"]
                                },
                                "reasoning": {
                                    "type": "string"
                                }
                            },
                            "required": ["queue", "priority", "category", "reasoning"],
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
                cancellationToken);

            sw.Stop();
            var usage = completion.Value.Usage;
            activity?.SetTag("llm.model", completion.Value.Model);
            activity?.SetTag("llm.input_tokens", usage.InputTokenCount);
            activity?.SetTag("llm.output_tokens", usage.OutputTokenCount);

            TicketingTelemetry.LlmCalls.Add(1, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("status", "success"));
            TicketingTelemetry.LlmDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("agent", "triage"));
            TicketingTelemetry.LlmTokens.Add(usage.InputTokenCount, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("type", "input"));
            TicketingTelemetry.LlmTokens.Add(usage.OutputTokenCount, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("type", "output"));

            var responseText = completion.Value.Content[0].Text;

            _logger.LogDebug("LLM response for {TicketId}: {Response}", ticket.TicketId, responseText);

            var decision = JsonSerializer.Deserialize<TriageDecision>(responseText, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize triage decision for ticket {ticket.TicketId}");

            _logger.LogInformation(
                "Triage decision for {TicketId}: Queue={Queue}, Priority={Priority}, Category={Category}",
                ticket.TicketId, decision.Queue, decision.Priority, decision.Category);

            return decision;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            sw.Stop();
            TicketingTelemetry.LlmCalls.Add(1, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("status", "transient_error"));
            _logger.LogWarning(ex, "Transient LLM error triaging ticket {TicketId}", ticket.TicketId);
            throw;
        }
        catch (JsonException ex)
        {
            sw.Stop();
            TicketingTelemetry.LlmCalls.Add(1, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("status", "parse_error"));
            _logger.LogError(ex, "Failed to parse LLM response for ticket {TicketId}", ticket.TicketId);
            throw;
        }
    }
}
