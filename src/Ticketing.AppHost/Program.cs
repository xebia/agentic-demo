var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);
var ticketingDb = sql.AddDatabase("TicketingDb");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(e => e.WithLifetime(ContainerLifetime.Persistent));
var blobs = storage.AddBlobs("AzureWebJobsStorage");

var serviceBus = builder.AddAzureServiceBus("ServiceBusConnection")
    .RunAsEmulator(e => e.WithLifetime(ContainerLifetime.Persistent));
var topic = serviceBus.AddServiceBusTopic("tickets-events", "tickets.events");
topic.AddServiceBusSubscription("triage-agent-subscription");
topic.AddServiceBusSubscription("purchasing-agent-subscription");
topic.AddServiceBusSubscription("fulfillment-agent-subscription");

// Auth service
var auth = builder.AddProject<Projects.Ticketing_Auth>("auth");

// Vendor Mock service
var vendorMock = builder.AddProject<Projects.Ticketing_VendorMock>("vendormock");

// Web app
var web = builder.AddProject<Projects.Ticketing_Web>("web")
    .WithReference(ticketingDb)
    .WithReference(serviceBus)
    .WithReference(auth)
    .WithEnvironment("ServiceBus__ConnectionString", serviceBus)
    .WithEnvironment("JwtSettings__AuthServiceUrl", auth.GetEndpoint("https"))
    .WaitFor(ticketingDb).WaitFor(auth);

// Chatbot
builder.AddProject<Projects.Ticketing_Chatbot>("chatbot")
    .WithReference(auth).WithReference(web)
    .WithEnvironment("ChatSettings__AuthServiceUrl", auth.GetEndpoint("https"))
    .WithEnvironment(ctx =>
    {
        ctx.EnvironmentVariables["ChatSettings__McpEndpointUrl"] =
            ReferenceExpression.Create($"{web.GetEndpoint("https")}/mcp");
    })
    .WaitFor(auth).WaitFor(web);

// TriageAgent (Azure Functions worker)
builder.AddProject<Projects.Ticketing_TriageAgent>("triageagent")
    .WithReference(serviceBus)
    .WithReference(blobs)
    .WithEnvironment("ServiceBusConnection", serviceBus)
    .WithEnvironment("ServiceBus__ConnectionString", serviceBus)
    .WithEnvironment("AuthService__Url", auth.GetEndpoint("https"))
    .WithEnvironment("TicketingApi__BaseUrl", web.GetEndpoint("https"))
    .WaitFor(serviceBus).WaitFor(auth).WaitFor(web).WaitFor(storage);

// FulfillmentAgent (Azure Functions worker — HTTP + Service Bus triggers)
var fulfillmentAgent = builder.AddProject<Projects.Ticketing_FulfillmentAgent>("fulfillmentagent")
    .WithReference(serviceBus)
    .WithReference(blobs)
    .WithEnvironment("ServiceBusConnection", serviceBus)
    .WithEnvironment("ServiceBus__ConnectionString", serviceBus)
    .WithEnvironment("AuthService__Url", auth.GetEndpoint("https"))
    .WithEnvironment("TicketingApi__BaseUrl", web.GetEndpoint("https"))
    .WithEnvironment("VendorApi__BaseUrl", vendorMock.GetEndpoint("https"))
    .WaitFor(serviceBus).WaitFor(auth).WaitFor(web).WaitFor(storage).WaitFor(vendorMock);

// PurchasingAgent (Azure Functions worker — Service Bus triggers)
builder.AddProject<Projects.Ticketing_PurchasingAgent>("purchasingagent")
    .WithReference(serviceBus)
    .WithReference(blobs)
    .WithEnvironment("ServiceBusConnection", serviceBus)
    .WithEnvironment("ServiceBus__ConnectionString", serviceBus)
    .WithEnvironment("AuthService__Url", auth.GetEndpoint("https"))
    .WithEnvironment("TicketingApi__BaseUrl", web.GetEndpoint("https"))
    .WithEnvironment("FulfillmentApi__BaseUrl", fulfillmentAgent.GetEndpoint("https"))
    .WaitFor(serviceBus).WaitFor(auth).WaitFor(web).WaitFor(storage).WaitFor(fulfillmentAgent);

// Vendor Mock needs to call back to Web API
vendorMock.WithEnvironment("CallbackApi__BaseUrl", web.GetEndpoint("https"));

builder.Build().Run();
