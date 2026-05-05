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
| **Orchestration** | .NET Aspire (containerized SQL Server, Azurite, Azure Service Bus emulator) |
| **UI Apps** | Blazor Web Apps (SSR + InteractiveServer) |
| **Business Logic** | CSLA .NET 10 |
| **Database** | SQL Server (Aspire container) / LocalDB (standalone fallback) / Azure SQL (prod) |
| **Agent Hosting** | Azure Functions (isolated worker) |
| **Agent Framework** | Microsoft Agent Framework (Chatbot) + custom message-bus agents |
| **Messaging** | Azure Service Bus |
| **AI Models** | Azure OpenAI (GPT-4o) |
| **Tool Protocol** | Model Context Protocol (MCP) |

## Solution Projects

| Project | Type | Port (HTTPS) | Description |
|---------|------|:------------:|-------------|
| `Ticketing.AppHost` | Aspire orchestrator | n/a | Boots all services + emulator containers |
| `Ticketing.ServiceDefaults` | Class library | n/a | Aspire shared defaults (OpenTelemetry, health, service discovery) |
| `Ticketing.Auth` | ASP.NET Core API | 7069 | Auth service (JWT tokens, JWKS, demo users, service accounts) |
| `Ticketing.Web` | Blazor Server + API | 7029 | Main app: Blazor UI, REST API, MCP server, alerts hub |
| `Ticketing.Chatbot` | Blazor Server | 7252 | Conversational support concierge — Microsoft Agent Framework (MAF) over MCP |
| `Ticketing.TriageAgent` | Azure Functions | n/a | Triages new tickets (Service Bus trigger + Azure OpenAI) |
| `Ticketing.PurchasingAgent` | Azure Functions | n/a | Quotes & approves purchase tickets, hands off to Fulfillment (Azure OpenAI) |
| `Ticketing.FulfillmentAgent` | Azure Functions | n/a | Catalog/quote API + order submission (HTTP + Service Bus) |
| `Ticketing.OperationsAgent` | Azure Functions | n/a | Health scans, DLQ + anomaly monitoring, alert posting (Azure OpenAI w/ rule-based fallback) |
| `Ticketing.VendorMock` | ASP.NET Minimal API | n/a | Simulated vendor: in-memory catalog + delayed-delivery callback |
| `Ticketing.Domain` | Class library | n/a | CSLA business objects and validation |
| `Ticketing.DataAccess` | Class library | n/a | EF Core implementation (SQL Server) |
| `Ticketing.DataAccess.Abstractions` | Class library | n/a | DAL interfaces and DTOs |
| `Ticketing.Messaging.Abstractions` | Class library | n/a | `IEventPublisher`, event envelope types |
| `Ticketing.Messaging.ServiceBus` | Class library | n/a | Azure Service Bus publisher implementation |

## Prerequisites

The recommended way to run the demo is via the Aspire AppHost (`dotnet run --project src/Ticketing.AppHost`), which orchestrates everything and pulls the local emulators it needs as Docker containers on first run.

**Required tools (install yourself):**

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview build is fine)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — Aspire uses it to run the emulators below. WSL2 backend on Windows is fine.
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local) — required because the four agents (Triage / Purchasing / Fulfillment / Operations) are Azure Functions isolated-worker projects. Install with `winget install Microsoft.Azure.FunctionsCoreTools` or `npm i -g azure-functions-core-tools@4 --unsafe-perm true`.

**Local emulators (Aspire pulls these automatically — no manual install):**

| Emulator | Container image | Used for |
|---|---|---|
| SQL Server | `mcr.microsoft.com/mssql/server` | `TicketingDb` database |
| Azurite (Azure Storage) | `mcr.microsoft.com/azure-storage/azurite` | Functions runtime state (`AzureWebJobsStorage`) |
| Azure Service Bus emulator | `mcr.microsoft.com/azure-messaging/servicebus-emulator` | The `tickets.events` topic and per-agent subscriptions |

> The Service Bus emulator has an EULA gate on first pull. Aspire accepts it for you; if a container fails to start with a EULA error, that's the cause.

**Required cloud service (not emulated):**

- **Azure OpenAI** resource with a chat-completion deployment (e.g., `gpt-4o`). The Chatbot and the Triage / Purchasing / Operations agents all need an endpoint + API key. See [Configure secrets](#3-configure-secrets) below.

**Optional — only if running projects standalone (not via Aspire):**

- [SQL Server LocalDB](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb) — `Ticketing.Web` falls back to LocalDB when run outside the AppHost. Included with Visual Studio or installable separately.
- An Azure Service Bus namespace — only needed if you want to skip the emulator and use a real cloud namespace. Manual setup steps are in [Set up Azure Service Bus](#2-set-up-azure-service-bus-only-without-aspire) below.

## Getting Started

### 1. Clone and build

```bash
git clone <repo-url>
cd agentic-demo
dotnet build src/Ticketing.slnx
```

### 2. Set up Azure Service Bus (only without Aspire)

> Skip this section if you're running via `dotnet run --project src/Ticketing.AppHost` — Aspire spins up a local Service Bus emulator container with the topic and subscriptions already configured. These manual steps only apply if you're pointing the services at a real Azure Service Bus namespace.

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

When running via the Aspire AppHost, infrastructure connection strings (Service Bus, Azurite, SQL Server) and inter-service URLs are injected into each project automatically. The **only** secrets you need to configure yourself are **Azure OpenAI credentials** for the four projects that talk to it: `Ticketing.Chatbot`, `Ticketing.TriageAgent`, `Ticketing.PurchasingAgent`, and `Ticketing.OperationsAgent`.

`Ticketing.FulfillmentAgent` and `Ticketing.VendorMock` don't need any secrets.

#### Ticketing.Web

Only needed if **not** using the AppHost (e.g., pointing at a real Azure Service Bus). Skip otherwise.

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

#### Azure Functions agents (TriageAgent, PurchasingAgent, OperationsAgent)

Azure Functions projects use `local.settings.json` for local config (the file is in `.gitignore`). Add the Azure OpenAI keys to the `Values` block of each agent's `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AZURE_FUNCTIONS_ENVIRONMENT": "Development",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "AuthService:ClientId": "triage-agent",
    "AuthService:ClientSecret": "secret-for-triage",
    "AzureOpenAI:Endpoint": "https://<resource>.openai.azure.com/",
    "AzureOpenAI:ApiKey": "<your-api-key>",
    "AzureOpenAI:DeploymentName": "gpt-4o"
  }
}
```

Repeat for `PurchasingAgent` (use `purchasing-agent` / `secret-for-purchasing`) and `OperationsAgent` (`operations-agent` / `secret-for-operations`). The same Azure OpenAI values can be reused across all three.

> When run under Aspire, `ServiceBusConnection`, `TicketingApi:BaseUrl`, `AuthService:Url`, etc., are injected as environment variables — don't add them to `local.settings.json`. Only fill them in if you're running the agent standalone with `func start` against a real Service Bus / Web app.

`Ticketing.FulfillmentAgent` only needs the `AuthService` client credentials (already set in its committed `local.settings.json`); it has no LLM dependency.

#### Ticketing.Auth

No secrets needed. Service accounts and auth settings are configured in `appsettings.json` with demo defaults:

| Service Account | Client ID | Client Secret | Roles |
|-----------------|-----------|---------------|-------|
| Triage Agent | `triage-agent` | `secret-for-triage` | Agent, HelpDesk |
| Purchasing Agent | `purchasing-agent` | `secret-for-purchasing` | Agent, HelpDesk |
| Fulfillment Agent | `fulfillment-agent` | `secret-for-fulfillment` | Agent, HelpDesk |
| Operations Agent | `operations-agent` | `secret-for-operations` | Agent, HelpDesk |

### 4. Run the solution

#### Recommended: Aspire AppHost (one command)

```bash
dotnet run --project src/Ticketing.AppHost
```

The AppHost spins up the SQL Server / Azurite / Service Bus emulator containers, starts the Auth service, Web app, Chatbot, VendorMock, and all four agents in dependency order, and opens the **Aspire dashboard** in your browser with traces, logs, and metrics for everything.

Useful endpoints once everything is up:
- Aspire dashboard: shown in the AppHost console output
- Blazor UI: `https://localhost:7029`
- Chatbot: `https://localhost:7252`
- API docs (Scalar): `https://localhost:7029/scalar/v1`
- MCP endpoint: `https://localhost:7029/mcp`
- Operations alerts: `https://localhost:7029/operations`

On first run, EF Core migrations apply automatically and demo data is seeded into `TicketingDb`.

#### Alternative: Run projects individually

Useful for focused debugging without Docker. The Web app falls back to LocalDB; events go nowhere unless you point `ServiceBus:ConnectionString` at a real namespace.

```bash
# In separate terminals, in this order:
dotnet run --project src/Ticketing.Auth --launch-profile https     # 1. Auth (other services need its JWKS)
dotnet run --project src/Ticketing.Web --launch-profile https      # 2. Web (DB auto-creates)
dotnet run --project src/Ticketing.Chatbot --launch-profile https  # 3. Chatbot (optional)
dotnet run --project src/Ticketing.VendorMock                      # 4. VendorMock (only needed for purchase flow)

# Each agent is an Azure Functions project — start with the Functions Core Tools:
cd src/Ticketing.TriageAgent && func start         # in its own terminal
cd src/Ticketing.PurchasingAgent && func start
cd src/Ticketing.FulfillmentAgent && func start
cd src/Ticketing.OperationsAgent && func start
```

### 5. Test the flow

The Chatbot is the easiest demo entry point: open `https://localhost:7252`, pick a demo user, and ask things like *"list my tickets"* or *"create a ticket for my laptop screen"*. The MAF agent uses MCP tools to drive the system; you'll see ticket changes propagate through the UI and Aspire traces.

For a script-driven walkthrough:

1. **Get a token** from the auth service:
   ```bash
   curl -X POST https://localhost:7069/token \
     -H "Content-Type: application/json" \
     -d '{"email": "alice@example.com"}'
   ```

2. **Create a support ticket**:
   ```bash
   curl -X POST https://localhost:7029/api/tickets \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <token>" \
     -d '{"title": "My laptop screen is cracked", "ticketType": "Support"}'
   ```
   The **TriageAgent** picks up the `ticket.created` event, calls Azure OpenAI, and updates the ticket: status → `Triaged`, with queue / priority / category / notes set.

3. **Create a purchase ticket** to exercise the multi-agent flow:
   ```bash
   curl -X POST https://localhost:7029/api/tickets \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <token>" \
     -d '{"title": "Need a new wireless mouse", "ticketType": "Purchase"}'
   ```
   **TriageAgent** routes it to the Purchasing queue → **PurchasingAgent** asks **FulfillmentAgent** for a quote, auto-approves if ≤ $500 → FulfillmentAgent submits the order to **VendorMock** → VendorMock posts a delivery callback → child Delivery tickets close → parent ticket auto-closes.

4. **Watch the Operations dashboard** at `https://localhost:7029/operations` — the **OperationsAgent** runs health probes every 2 minutes and posts alerts for DLQ buildup, SLA violations, or event-burst anomalies. The badge in the nav bar updates live.

5. **Verify** any ticket with:
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

### Azure Functions agents (`local.settings.json`)

These settings apply to `Ticketing.TriageAgent`, `Ticketing.PurchasingAgent`, `Ticketing.FulfillmentAgent`, and `Ticketing.OperationsAgent`. When running under Aspire, every entry marked *(injected)* is filled in by the AppHost — only the Azure OpenAI keys need to be set manually (and only on the agents that use them).

| Setting | Default | Source | Used by |
|---------|---------|--------|---------|
| `ServiceBusConnection` | *(injected)* | Aspire | Triage, Purchasing, Fulfillment, Operations (Service Bus trigger binding) |
| `ServiceBus:ConnectionString` | *(injected)* | Aspire | Triage, Purchasing, Fulfillment (event publisher) |
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` | local file | All four (Functions runtime state) |
| `AuthService:Url` | *(injected)* | Aspire | All four |
| `AuthService:ClientId` | `<agent>-agent` | local file | All four |
| `AuthService:ClientSecret` | `secret-for-<agent>` | local file | All four |
| `TicketingApi:BaseUrl` | *(injected)* | Aspire | All four |
| `VendorApi:BaseUrl` | *(injected)* | Aspire | Fulfillment, Operations |
| `FulfillmentApi:BaseUrl` | *(injected)* | Aspire | Purchasing |
| `AzureOpenAI:Endpoint` | *(none)* | **you set** | Triage, Purchasing, Operations |
| `AzureOpenAI:ApiKey` | *(none)* | **you set** | Triage, Purchasing, Operations |
| `AzureOpenAI:DeploymentName` | `gpt-4o` | local file | Triage, Purchasing, Operations |

## Contributing

(To be added in future phases)

## License

See [LICENSE](LICENSE) file for details.
