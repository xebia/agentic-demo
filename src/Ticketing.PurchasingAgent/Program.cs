using System.Reflection;
using Azure.Storage.Blobs;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ticketing.Messaging.ServiceBus;
using Ticketing.PurchasingAgent.Functions;
using Ticketing.PurchasingAgent.Services;
using Ticketing.PurchasingAgent.Workflow;
using Ticketing.PurchasingAgent.Workflow.Executors;

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

// MAF workflow executors — transient so each workflow Build gets fresh instances.
builder.Services.AddTransient<FetchTicketExecutor>();
builder.Services.AddTransient<AnalyzeRequestExecutor>();
builder.Services.AddTransient<GetQuoteExecutor>();
builder.Services.AddTransient<DecideExecutor>();
builder.Services.AddTransient<ApprovalBridgeExecutor>();
builder.Services.AddTransient<ApplyApprovalExecutor>();
builder.Services.AddTransient<EscalateExecutor>();
builder.Services.AddScoped<PurchasingWorkflowFactory>();

// Workflow checkpoint persistence — suspended workflows survive function restarts
// by serializing their state to Azure Blob storage (AzureWebJobsStorage).
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["AzureWebJobsStorage"]
        ?? throw new InvalidOperationException("AzureWebJobsStorage is not configured");
    return new BlobServiceClient(connectionString);
});
builder.Services.AddSingleton<BlobWorkflowCheckpointStore>();
builder.Services.AddSingleton<CheckpointManager>(sp =>
    CheckpointManager.CreateJson(
        sp.GetRequiredService<BlobWorkflowCheckpointStore>(),
        customOptions: null));

builder.Build().Run();
