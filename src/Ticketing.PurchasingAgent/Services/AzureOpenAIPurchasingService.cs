using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
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

        var responseText = completion.Value.Content[0].Text;

        _logger.LogDebug("LLM response for {TicketId}: {Response}", ticket.TicketId, responseText);

        var decision = JsonSerializer.Deserialize<PurchasingDecision>(responseText, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize purchasing decision for ticket {ticket.TicketId}");

        _logger.LogInformation(
            "Purchasing decision for {TicketId}: {ItemCount} items, autoApprove={AutoApprove}",
            ticket.TicketId, decision.Items.Count, decision.AutoApproveRecommendation);

        return decision;
    }
}
