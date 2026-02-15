using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Ticketing.Messaging.Abstractions.Diagnostics;
using Ticketing.PurchasingAgent.Models;

namespace Ticketing.PurchasingAgent.Services;

public class AzureOpenAIPurchasingService : IPurchasingService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAIPurchasingService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SystemPrompt = """
        You are a purchasing analyst for an organization's internal IT procurement system.
        Your job is to analyze purchase requests and identify what items need to be ordered.

        ## Analysis Guidelines

        1. Read the ticket title and description carefully
        2. Identify specific items that need to be purchased
        3. For each item, provide a short description that can be used to search a hardware catalog
        4. Use common product terms (e.g., "laptop", "monitor", "keyboard", "mouse", "headset", "webcam", "docking station")
        5. If the request mentions specific specs (e.g., "developer laptop", "4K monitor"), include those qualifiers

        ## Auto-Approve Recommendation

        Recommend auto-approval (autoApproveRecommendation = true) when:
        - The request is for standard equipment (keyboards, mice, headsets, webcams, standard monitors)
        - The items are clearly for legitimate business use
        - The request is straightforward with no unusual aspects

        Recommend manager approval (autoApproveRecommendation = false) when:
        - The request is for premium/executive equipment
        - The request seems unusual or excessive
        - Multiple expensive items are requested together
        - The request lacks clear business justification

        Note: The final approval decision also depends on the total cost (auto-approve ≤ $500),
        but your recommendation helps inform the decision regardless of cost.

        ## Response Format

        Respond with a JSON object containing:
        - items: array of { "description": "search term for catalog", "quantity": number }
        - reasoning: Brief explanation of your analysis and recommendation
        - autoApproveRecommendation: true/false based on the guidelines above
        """;

    public AzureOpenAIPurchasingService(
        IConfiguration configuration,
        ILogger<AzureOpenAIPurchasingService> logger)
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

    public async Task<PurchasingDecision> AnalyzePurchaseRequestAsync(
        TicketDetailResponse ticket,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing purchase request {TicketId}: {Title}", ticket.TicketId, ticket.Title);

        using var activity = TicketingTelemetry.Source.StartActivity("openai.chat.completion");
        activity?.SetTag("ticket.id", ticket.TicketId);
        activity?.SetTag("agent", "purchasing");
        var sw = Stopwatch.StartNew();

        try
        {
            var userMessage = $"""
                Ticket ID: {ticket.TicketId}
                Type: {ticket.TicketType}
                Title: {ticket.Title}
                Description: {ticket.Description ?? "(no description)"}
                Priority: {ticket.Priority}
                Category: {ticket.Category ?? "(not set)"}
                Requested By: {ticket.CreatedByName ?? ticket.CreatedBy}
                """;

            var options = new ChatCompletionOptions
            {
                Temperature = 0.1f,
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "purchasing_decision",
                    BinaryData.FromString("""
                        {
                            "type": "object",
                            "properties": {
                                "items": {
                                    "type": "array",
                                    "items": {
                                        "type": "object",
                                        "properties": {
                                            "description": { "type": "string" },
                                            "quantity": { "type": "integer" }
                                        },
                                        "required": ["description", "quantity"],
                                        "additionalProperties": false
                                    }
                                },
                                "reasoning": { "type": "string" },
                                "autoApproveRecommendation": { "type": "boolean" }
                            },
                            "required": ["items", "reasoning", "autoApproveRecommendation"],
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

            TicketingTelemetry.LlmCalls.Add(1, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("status", "success"));
            TicketingTelemetry.LlmDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("agent", "purchasing"));
            TicketingTelemetry.LlmTokens.Add(usage.InputTokenCount, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("type", "input"));
            TicketingTelemetry.LlmTokens.Add(usage.OutputTokenCount, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("type", "output"));

            var responseText = completion.Value.Content[0].Text;

            _logger.LogDebug("LLM response for {TicketId}: {Response}", ticket.TicketId, responseText);

            var decision = JsonSerializer.Deserialize<PurchasingDecision>(responseText, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize purchasing decision for ticket {ticket.TicketId}");

            _logger.LogInformation(
                "Purchasing decision for {TicketId}: {ItemCount} items, autoApprove={AutoApprove}",
                ticket.TicketId, decision.Items.Count, decision.AutoApproveRecommendation);

            return decision;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            sw.Stop();
            TicketingTelemetry.LlmCalls.Add(1, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("status", "transient_error"));
            _logger.LogWarning(ex, "Transient LLM error analyzing ticket {TicketId}", ticket.TicketId);
            throw;
        }
        catch (JsonException ex)
        {
            sw.Stop();
            TicketingTelemetry.LlmCalls.Add(1, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("status", "parse_error"));
            _logger.LogError(ex, "Failed to parse LLM response for ticket {TicketId}", ticket.TicketId);
            throw;
        }
    }
}
