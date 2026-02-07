using Ticketing.Chatbot.Components;
using Ticketing.Chatbot.Models;
using Ticketing.Chatbot.Services;
using Microsoft.Extensions.AI;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure chat settings
builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("ChatSettings"));

// Register services
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddHttpClient<AuthService>();
builder.Services.AddHttpClient<McpClientService>();
builder.Services.AddScoped<McpToolProvider>();

// Configure Azure OpenAI chat client
var azureEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var azureApiKey = builder.Configuration["AzureOpenAI:ApiKey"];
var azureDeployment = builder.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4-turbo";

if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureApiKey))
{
    Console.WriteLine($"Configuring Azure OpenAI - Endpoint: {azureEndpoint}, Deployment: {azureDeployment}");

    var azureClient = new AzureOpenAIClient(
        new Uri(azureEndpoint),
        new AzureKeyCredential(azureApiKey)
    );

    // Get the ChatClient for the deployment, then wrap it as IChatClient
    var openAIChatClient = azureClient.GetChatClient(azureDeployment);

    // Wrap with FunctionInvokingChatClient to enable automatic tool invocation
    IChatClient chatClient = new ChatClientBuilder(openAIChatClient.AsIChatClient())
        .UseFunctionInvocation()
        .Build();

    builder.Services.AddSingleton(chatClient);
    builder.Services.AddScoped<ChatService>();
}
else
{
    Console.WriteLine("WARNING: Azure OpenAI not configured. Missing Endpoint or ApiKey.");
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
