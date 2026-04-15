using System.Reflection;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ticketing.Messaging.ServiceBus;
using Ticketing.PurchasingAgent.Functions;
using Ticketing.PurchasingAgent.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);

builder.AddServiceDefaults();

// Register auth token provider (singleton — manages its own token cache)
builder.Services.AddSingleton<AuthTokenProvider>();
builder.Services.AddHttpClient<AuthTokenProvider>();

// Register ticketing API client with typed HttpClient
builder.Services.AddHttpClient<TicketingApiClient>();

// Register fulfillment API client with typed HttpClient (for quotes)
builder.Services.AddHttpClient<FulfillmentApiClient>();

// Register Azure OpenAI purchasing service
builder.Services.AddSingleton<IPurchasingService, AzureOpenAIPurchasingService>();

// Register Service Bus messaging for publishing events
builder.Services.AddServiceBusMessaging(builder.Configuration);

// Register PurchasingFunction so StartupScanFunction can reuse its core logic
builder.Services.AddScoped<PurchasingFunction>();

builder.Build().Run();
