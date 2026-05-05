using Azure;
using Azure.AI.OpenAI;
using Ticketing.Chatbot.Components;
using Ticketing.Chatbot.Models;
using Ticketing.Chatbot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("ChatSettings"));

// Per-circuit user/auth state.
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddHttpClient<AuthService>();

// Configure Azure OpenAI ChatClient as a singleton; the MAF agent is built per-circuit
// from this shared client so per-user MCP auth can flow into the agent's tool set.
var azureEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var azureApiKey = builder.Configuration["AzureOpenAI:ApiKey"];
var azureDeployment = builder.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4-turbo";

if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureApiKey))
{
    Console.WriteLine($"Configuring Azure OpenAI - Endpoint: {azureEndpoint}, Deployment: {azureDeployment}");

    var azureClient = new AzureOpenAIClient(new Uri(azureEndpoint), new AzureKeyCredential(azureApiKey));
    var chatClient = azureClient.GetChatClient(azureDeployment);

    builder.Services.AddSingleton(chatClient);
    builder.Services.AddScoped<ChatService>();
}
else
{
    Console.WriteLine("WARNING: Azure OpenAI not configured. Missing Endpoint or ApiKey.");
}

var app = builder.Build();

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
