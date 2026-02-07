using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ticketing.FulfillmentAgent.Functions;
using Ticketing.FulfillmentAgent.Services;
using Ticketing.Messaging.ServiceBus;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.AddServiceDefaults();

// Register auth token provider (singleton — manages its own token cache)
builder.Services.AddSingleton<AuthTokenProvider>();
builder.Services.AddHttpClient<AuthTokenProvider>();

// Register ticketing API client with typed HttpClient
builder.Services.AddHttpClient<TicketingApiClient>();

// Register vendor API client with typed HttpClient
builder.Services.AddHttpClient<VendorApiClient>();

// Register Service Bus messaging for publishing events
builder.Services.AddServiceBusMessaging(builder.Configuration);

// Register FulfillmentFunction so StartupScanFunction can reuse its core logic
builder.Services.AddScoped<FulfillmentFunction>();

builder.Build().Run();
