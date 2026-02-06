# Agentic Demo - IT Support & Purchasing System

Demo solution based on agents, Agent-to-Agent (A2A) communication, and async messaging for IT support and purchasing workflows.

## Overview

This system demonstrates a modern, agent-based approach to handling IT support tickets and purchasing requests. It combines human actors (employees, help desk staff, approvers) with intelligent agents (ticketing triage, purchasing policy enforcement, fulfillment) to create an efficient, scalable support and procurement system.

## Quick Links

- [Design Documentation](design/README.md) - System architecture, components, and design decisions
- [Ticketing System](design/ticketing-system.md) - Data model, APIs, and UI specifications
- [Async Messaging Patterns](design/async-messaging-patterns.md) - Event publishing and queue patterns

## Technology Stack

| Component | Technology |
|-----------|------------|
| **Runtime** | .NET 10 |
| **UI Apps** | Blazor Web Apps (SSR + InteractiveServer) |
| **Business Logic** | CSLA .NET 10 |
| **Database** | SQL Server LocalDB (dev) / Azure SQL (prod) |
| **Agent Hosting** | Azure Functions (isolated worker) |
| **Messaging** | Azure Service Bus |
| **AI Models** | Azure OpenAI (GPT-4o) |

## Solution Projects

| Project | Type | Port (HTTPS) | Description |
|---------|------|:------------:|-------------|
| `Ticketing.Auth` | ASP.NET Core API | 7069 | Auth service (JWT tokens, JWKS, demo users, service accounts) |
| `Ticketing.Web` | Blazor Server + API | 7029 | Main app: Blazor UI, REST API, MCP server |
| `Ticketing.Chatbot` | Blazor Server | 7252 | AI chatbot UI that uses MCP tools |
| `Ticketing.TriageAgent` | Azure Functions | n/a | Automatically triages new tickets via Azure OpenAI |
| `Ticketing.Domain` | Class library | n/a | CSLA business objects and validation |
| `Ticketing.DataAccess` | Class library | n/a | EF Core implementation (SQL Server) |
| `Ticketing.DataAccess.Abstractions` | Class library | n/a | DAL interfaces and DTOs |
| `Ticketing.Messaging.Abstractions` | Class library | n/a | `IEventPublisher`, event envelope types |
| `Ticketing.Messaging.ServiceBus` | Class library | n/a | Azure Service Bus publisher implementation |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [SQL Server LocalDB](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb) (included with Visual Studio or installable separately)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local) (for running the TriageAgent locally)
- An Azure subscription with:
  - **Azure OpenAI** resource with a `gpt-4o` deployment
  - **Azure Service Bus** namespace (Standard tier or higher for topics)

## Getting Started

### 1. Clone and build

```bash
git clone <repo-url>
cd agentic-demo
dotnet build src/Ticketing.slnx
```

### 2. Set up Azure Service Bus

Create these resources in your Azure Service Bus namespace:

| Resource | Name | Notes |
|----------|------|-------|
| **Topic** | `tickets.events` | All ticket events are published here |
| **Subscription** | `triage-agent-subscription` | Under the `tickets.events` topic |
| **Subscription filter** | SQL filter: `Subject = 'ticket.created'` | So the triage agent only receives new-ticket events |

You can create these via the Azure portal or the Azure CLI:

```bash
# Replace with your Service Bus namespace
SB_NAMESPACE="your-namespace"
RESOURCE_GROUP="your-rg"

az servicebus topic create \
  --namespace-name $SB_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --name tickets.events

az servicebus topic subscription create \
  --namespace-name $SB_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --topic-name tickets.events \
  --name triage-agent-subscription

# Remove the default "match all" rule and add the filter
az servicebus topic subscription rule delete \
  --namespace-name $SB_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --topic-name tickets.events \
  --subscription-name triage-agent-subscription \
  --name '$Default'

az servicebus topic subscription rule create \
  --namespace-name $SB_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --topic-name tickets.events \
  --subscription-name triage-agent-subscription \
  --name ticket-created-filter \
  --filter-sql-expression "Subject = 'ticket.created'"
```

Get the connection string for configuration:

```bash
az servicebus namespace authorization-rule keys list \
  --namespace-name $SB_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --name RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv
```

### 3. Configure secrets

Each project that needs secrets should use `dotnet user-secrets` to keep credentials out of source control.

#### Ticketing.Web

```bash
cd src/Ticketing.Web
dotnet user-secrets init  # only needed once
dotnet user-secrets set "ServiceBus:ConnectionString" "<your-service-bus-connection-string>"
```

The database connection string and auth service URL have working defaults for local development in `appsettings.json` (LocalDB + `https://localhost:7069`).

#### Ticketing.Chatbot

The Chatbot project already has a `UserSecretsId`. Set the Azure OpenAI credentials:

```bash
cd src/Ticketing.Chatbot
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-api-key>"
dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-4o"
```

#### Ticketing.TriageAgent

Azure Functions use `local.settings.json` for local development (this file is `.gitignore`-friendly). Edit `src/Ticketing.TriageAgent/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ServiceBusConnection": "<your-service-bus-connection-string>",
    "ServiceBus:ConnectionString": "<your-service-bus-connection-string>",
    "AuthService:Url": "https://localhost:7069",
    "AuthService:ClientId": "triage-agent",
    "AuthService:ClientSecret": "secret-for-triage",
    "TicketingApi:BaseUrl": "https://localhost:7029",
    "AzureOpenAI:Endpoint": "https://<resource>.openai.azure.com/",
    "AzureOpenAI:ApiKey": "<your-api-key>",
    "AzureOpenAI:DeploymentName": "gpt-4o"
  }
}
```

> `ServiceBusConnection` is used by the Azure Functions Service Bus trigger binding. `ServiceBus:ConnectionString` is used by the `IEventPublisher` for publishing events. Both need the same value.

#### Ticketing.Auth

No secrets needed. Service accounts and auth settings are configured in `appsettings.json` with demo defaults:

| Service Account | Client ID | Client Secret | Roles |
|-----------------|-----------|---------------|-------|
| Triage Agent | `triage-agent` | `secret-for-triage` | Agent, HelpDesk |
| Fulfillment Agent | `fulfillment-agent` | `secret-for-fulfillment` | Agent, HelpDesk |

### 4. Run the solution

The services must be started in this order because of startup dependencies:

**Step 1: Start the Auth service** (other services depend on its JWKS endpoint)

```bash
dotnet run --project src/Ticketing.Auth --launch-profile https
```

Verify: `https://localhost:7069` should return a health-check JSON. The JWKS endpoint at `https://localhost:7069/.well-known/jwks.json` must be reachable before starting other services.

**Step 2: Start the Web app** (database auto-creates on first run)

```bash
dotnet run --project src/Ticketing.Web --launch-profile https
```

On first run, EF Core migrations run automatically and demo data is seeded. Verify:
- Blazor UI: `https://localhost:7029`
- API docs (Scalar): `https://localhost:7029/scalar/v1`
- MCP endpoint: `https://localhost:7029/mcp`

**Step 3 (optional): Start the Chatbot**

```bash
dotnet run --project src/Ticketing.Chatbot --launch-profile https
```

Verify: `https://localhost:7252`

**Step 4 (optional): Start the Triage Agent**

```bash
cd src/Ticketing.TriageAgent
func start
```

On startup, the agent will:
1. Scan for any existing tickets with status `New` and triage them
2. Listen for `ticket.created` events on Service Bus to triage new tickets in real-time

### 5. Test the flow

1. **Get a token** from the auth service:
   ```bash
   curl -X POST https://localhost:7069/token \
     -H "Content-Type: application/json" \
     -d '{"email": "alice@example.com"}'
   ```

2. **Create a ticket** via the REST API:
   ```bash
   curl -X POST https://localhost:7029/api/tickets \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <token>" \
     -d '{"title": "My laptop screen is cracked", "ticketType": "Support"}'
   ```

3. If the Triage Agent is running, it will automatically:
   - Receive the `ticket.created` event via Service Bus
   - Fetch the ticket details via REST API
   - Analyze the ticket with Azure OpenAI
   - Update the ticket: status -> `Triaged`, assign queue, set priority/category, add triage notes

4. **Verify triage** by fetching the ticket again:
   ```bash
   curl https://localhost:7029/api/tickets/<ticket-id> \
     -H "Authorization: Bearer <token>"
   ```

## EF Core Migrations

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> \
  --project src/Ticketing.DataAccess \
  --startup-project src/Ticketing.Web

# Apply migrations manually (auto-applied in Development mode)
dotnet ef database update \
  --project src/Ticketing.DataAccess \
  --startup-project src/Ticketing.Web

# Reset database (auto-recreated on next run)
dotnet ef database drop --force \
  --project src/Ticketing.DataAccess \
  --startup-project src/Ticketing.Web
```

## Configuration Reference

### Ticketing.Auth (`appsettings.json`)

| Setting | Default | Description |
|---------|---------|-------------|
| `AuthSettings:Issuer` | `https://auth.ticketing.local` | JWT issuer claim |
| `AuthSettings:Audience` | `ticketing-api` | JWT audience claim |
| `AuthSettings:TokenLifetimeMinutes` | `60` | User token lifetime |
| `AuthSettings:ServiceAccountTokenLifetimeMinutes` | `30` | Service account token lifetime |

### Ticketing.Web (`appsettings.json` + user secrets)

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectionStrings:TicketingDb` | LocalDB connection | SQL Server connection string |
| `JwtSettings:AuthServiceUrl` | `https://localhost:7069` | Auth service URL for JWKS |
| `JwtSettings:Issuer` | `https://auth.ticketing.local` | Expected JWT issuer |
| `JwtSettings:Audience` | `ticketing-api` | Expected JWT audience |
| `ServiceBus:ConnectionString` | *(empty)* | Azure Service Bus connection string |

### Ticketing.Chatbot (user secrets)

| Setting | Default | Description |
|---------|---------|-------------|
| `ChatSettings:AuthServiceUrl` | `https://localhost:7069` | Auth service URL |
| `ChatSettings:McpEndpointUrl` | `https://localhost:7029/mcp` | MCP server endpoint |
| `AzureOpenAI:Endpoint` | *(none)* | Azure OpenAI resource endpoint |
| `AzureOpenAI:ApiKey` | *(none)* | Azure OpenAI API key |
| `AzureOpenAI:DeploymentName` | `gpt-4-turbo` | Chat model deployment name |

### Ticketing.TriageAgent (`local.settings.json`)

| Setting | Default | Description |
|---------|---------|-------------|
| `ServiceBusConnection` | *(empty)* | Service Bus connection (trigger binding) |
| `ServiceBus:ConnectionString` | *(empty)* | Service Bus connection (event publisher) |
| `AuthService:Url` | `https://localhost:7069` | Auth service URL |
| `AuthService:ClientId` | `triage-agent` | Service account client ID |
| `AuthService:ClientSecret` | `secret-for-triage` | Service account secret |
| `TicketingApi:BaseUrl` | `https://localhost:7029` | Web API base URL |
| `AzureOpenAI:Endpoint` | *(none)* | Azure OpenAI resource endpoint |
| `AzureOpenAI:ApiKey` | *(none)* | Azure OpenAI API key |
| `AzureOpenAI:DeploymentName` | `gpt-4o` | Chat model deployment name |

## Contributing

(To be added in future phases)

## License

See [LICENSE](LICENSE) file for details.
