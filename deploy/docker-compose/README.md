# Docker Compose Deployment

Run the full Ticketing System stack locally using Docker Compose — no .NET SDK, Aspire, or LocalDB required.

## Prerequisites

- **Docker Desktop** (v4.20+) with Linux containers enabled
- **8 GB+ RAM** allocated to Docker (10+ services including SQL Server)
- **(Optional) Azure OpenAI** credentials for AI-powered agents and chatbot

## Quick Start

All commands below are run from the **repository root**.

### 1. Create your `.env` file

```bash
cp deploy/docker-compose/.env.example deploy/docker-compose/.env
```

Edit `deploy/docker-compose/.env` and set:

| Variable | Required | Description |
| --------- | -------- | ------------- |
| `SA_PASSWORD` | Yes | SQL Server SA password. Must meet [complexity requirements](https://learn.microsoft.com/en-us/sql/relational-databases/security/password-policy): 8+ characters with uppercase, lowercase, digit, and special character. |
| `AZURE_OPENAI_ENDPOINT` | No | Azure OpenAI endpoint URL (e.g., `https://my-resource.openai.azure.com/`) |
| `AZURE_OPENAI_API_KEY` | No | Azure OpenAI API key |
| `AZURE_OPENAI_DEPLOYMENT` | No | Model deployment name (default: `gpt-4-turbo`) |

> Without Azure OpenAI credentials, the system runs normally but agents and chatbot will skip AI features and log warnings.

### 2. Build and start all services

```bash
docker compose -f deploy/docker-compose/docker-compose.yml up --build
```

First build takes a few minutes. Subsequent starts are much faster.

### 3. Access the application

| Service | URL | Notes |
| --------- | ----- | ------- |
| **Web App (Blazor)** | http://localhost:5254 | Main ticketing UI with mock auth |
| **API Docs (Scalar)** | http://localhost:5254/scalar/v1 | Interactive REST API explorer |
| **Chatbot** | http://localhost:5252 | AI chatbot (requires Azure OpenAI) |
| **Auth Service** | http://localhost:5069 | JWT token service |
| **SQL Server** | `localhost:1433` | Connect with SSMS/Azure Data Studio (user: `sa`) |

The web app automatically runs EF Core migrations and seeds demo data on first startup.

## Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│  Infrastructure                                             │
│  ┌──────────┐  ┌─────────────┐  ┌──────────┐                │
│  │ SQL      │  │ Service Bus │  │ Azurite  │                │
│  │ Server   │  │ Emulator    │  │ (Storage)│                │
│  └──────────┘  └─────────────┘  └──────────┘                │
├─────────────────────────────────────────────────────────────┤
│  Application                                                │
│  ┌──────┐  ┌──────┐  ┌────────────┐  ┌────────┐             │
│  │ Auth │  │ Web  │  │ VendorMock │  │Chatbot │             │
│  └──────┘  └──────┘  └────────────┘  └────────┘             │
├─────────────────────────────────────────────────────────────┤
│  AI Agent Workers (Azure Functions)                         │
│  ┌────────┐ ┌────────────┐ ┌─────────────┐ ┌────────────┐   │
│  │ Triage │ │ Purchasing │ │ Fulfillment │ │ Operations │   │
│  └────────┘ └────────────┘ └─────────────┘ └────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## Common Operations

### Stop all services

```bash
docker compose -f deploy/docker-compose/docker-compose.yml down
```

### Stop and remove all data (clean slate)

```bash
docker compose -f deploy/docker-compose/docker-compose.yml down -v
```

### Rebuild a single service

```bash
docker compose -f deploy/docker-compose/docker-compose.yml up --build web
```

### View logs for a specific service

```bash
docker compose -f deploy/docker-compose/docker-compose.yml logs -f web
docker compose -f deploy/docker-compose/docker-compose.yml logs -f triage-agent
```

### Run without AI agents (lighter footprint)

To run just the core application without AI agent workers:

```bash
docker compose -f deploy/docker-compose/docker-compose.yml up --build \
  sql azurite servicebus auth web vendormock
```

## Getting an API Token

The auth service provides JWT tokens for API access. Get a token using curl:

```bash
# User token
curl -X POST http://localhost:5069/token \
  -H "Content-Type: application/json" \
  -d '{"email": "alice@example.com"}'

# Service account token
curl -X POST http://localhost:5069/token/client-credentials \
  -H "Content-Type: application/json" \
  -d '{"clientId": "triage-agent", "clientSecret": "secret-for-triage"}'
```

Use the returned token in API requests:

```bash
curl http://localhost:5254/api/tickets \
  -H "Authorization: Bearer <token>"
```

## Troubleshooting

### SQL Server won't start

- Ensure `SA_PASSWORD` meets complexity requirements (8+ chars, mixed case, digit, special char)
- Check Docker has enough memory allocated (SQL Server needs ~2 GB)

### Service Bus emulator fails

- The emulator needs SQL Server to be fully healthy first — it will retry automatically
- If it keeps failing, try `docker compose down -v` to reset volumes and start fresh

### Web app can't connect to SQL

- SQL Server takes ~30s to initialize on first run — the web app has health check dependencies and will wait
- Check that the `SA_PASSWORD` in `.env` hasn't changed after volumes were created (drop volumes to reset)

### Agents not processing messages

- Verify Service Bus emulator is running: `docker compose logs servicebus`
- Check agent logs: `docker compose logs triage-agent`
- Agents need the Service Bus emulator to be fully initialized with topics and subscriptions

### Build failures

- This project uses .NET 10 preview — ensure your Docker images pull successfully
- If NuGet restore fails, check your network connection (packages are restored during build)

### Port conflicts

- Default ports: `5254` (web), `5069` (auth), `5252` (chatbot), `1433` (SQL)
- If ports conflict, edit the `ports` section in `docker-compose.yml`
