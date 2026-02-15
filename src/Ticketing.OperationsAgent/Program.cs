using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ticketing.Messaging.ServiceBus;
using Ticketing.OperationsAgent.Services;

var builder = FunctionsApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddSingleton<AuthTokenProvider>();
builder.Services.AddHttpClient<AuthTokenProvider>();
builder.Services.AddHttpClient<TicketingApiClient>();
builder.Services.AddHttpClient<AlertApiClient>();
builder.Services.AddSingleton<HealthCheckService>();
builder.Services.AddSingleton<DlqMonitorService>();
builder.Services.AddSingleton<IOperationsAnalyzer, AzureOpenAIOperationsAnalyzer>();
builder.Services.AddServiceBusMessaging(builder.Configuration);

builder.Build().Run();
