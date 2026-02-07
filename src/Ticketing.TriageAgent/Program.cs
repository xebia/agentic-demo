using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ticketing.Messaging.ServiceBus;
using Ticketing.TriageAgent.Functions;
using Ticketing.TriageAgent.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register auth token provider (singleton — manages its own token cache)
builder.Services.AddSingleton<AuthTokenProvider>();
builder.Services.AddHttpClient<AuthTokenProvider>();

// Register ticketing API client with typed HttpClient
builder.Services.AddHttpClient<TicketingApiClient>();

// Register Azure OpenAI triage service
builder.Services.AddSingleton<ITriageService, AzureOpenAITriageService>();

// Register Service Bus messaging for publishing events
builder.Services.AddServiceBusMessaging(builder.Configuration);

// Register TriageFunction so StartupScanFunction can reuse its core logic
builder.Services.AddScoped<TriageFunction>();

builder.Build().Run();
