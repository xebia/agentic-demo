# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Agentic Demo — an IT support and purchasing system demonstrating agent-based architecture, A2A communication, and async messaging patterns. Built with .NET 10, CSLA .NET 10, Blazor, and Entity Framework Core against SQL Server LocalDB.

## Build & Run Commands

```bash
# Build the solution
dotnet build src/Ticketing.slnx

# Run the web app (database auto-creates with migrations + demo data)
dotnet run --project src/Ticketing.Web

# Run with watch mode
dotnet watch run --project src/Ticketing.Web

# Restore dependencies
dotnet restore src/Ticketing.slnx
```

**Dev URLs**: http://localhost:5254 | https://localhost:7029
**API docs (Scalar)**: https://localhost:7029/scalar/v1

## EF Core Migrations

```bash
# Add a new migration (run from repo root)
dotnet ef migrations add <MigrationName> --project src/Ticketing.DataAccess --startup-project src/Ticketing.Web

# Apply migrations manually
dotnet ef database update --project src/Ticketing.DataAccess --startup-project src/Ticketing.Web

# Reset database (auto-recreated on next run)
dotnet ef database drop --project src/Ticketing.DataAccess --startup-project src/Ticketing.Web --force
```

Migrations run automatically on startup in Development mode.

## Tests

No test projects exist yet. When added:
```bash
dotnet test src/Ticketing.slnx
```

## Solution Architecture

Four projects in `src/Ticketing.slnx`:

- **Ticketing.Domain** — CSLA 10 business objects (`TicketEdit`, `TicketInfo`, `TicketList`). All business logic, validation rules, and data portal operations live here.
- **Ticketing.DataAccess.Abstractions** — DAL interfaces (`ITicketEditDal`, `ITicketListDal`) and DTOs. No implementation dependencies.
- **Ticketing.DataAccess** — EF Core implementation of DAL interfaces. `TicketingDbContext`, entity configurations, migrations, and `DemoDataSeeder`.
- **Ticketing.Web** — Blazor Server app (SSR + InteractiveServer) with REST API (`TicketsController`). Dual auth: JWT Bearer for API, Cookie for Blazor with mock auth for demo.

**Dependency flow**: Web → Domain → DataAccess.Abstractions ← DataAccess

## CSLA .NET 10 Patterns (Critical)

This project uses CSLA 10 with modern partial property syntax. **Always use this pattern, never the legacy `RegisterProperty` pattern.**

```csharp
[CslaImplementProperties]
public partial class MyObject : BusinessBase<MyObject>
{
    [Required]
    [StringLength(200)]
    public partial string Name { get; set; }
    public partial int Id { get; private set; }

    [Fetch]
    private void Fetch(int id, [Inject] IMyDal dal) { /* ... */ }

    [Insert]
    private void Insert([Inject] IMyDal dal) { /* ... */ }
}
```

Key rules:
- Use `[CslaImplementProperties]` attribute on all business classes
- Use data annotations (`[Required]`, `[StringLength]`, etc.) for validation
- Use `[Inject]` for DI in data portal methods (`[Create]`, `[Fetch]`, `[Insert]`, `[Update]`, `[Delete]`)
- Read-only children use `[FetchChild]` with data passed in (no DB call) to avoid N+1
- Lists fetch all data in ONE query, then pass rows to children via `IChildDataPortal<T>.FetchChildAsync(data)`

CSLA stereotypes: `BusinessBase<T>` (editable), `ReadOnlyBase<T>` (read-only), `ReadOnlyListBase<T,C>` (lists), `CommandBase<T>` (commands).

## Data Model

Core entities: `Tickets` and `TicketHistory` (audit trail). Tickets use string IDs (`TKT-12345` format), string-based enums for type/status/priority/category/queue, and a self-referential `ParentTicketId` for ticket hierarchies.

**Status workflow**: `new → triaged → in-progress → resolved → closed` (support path) or `new → triaged → pending-approval → approved/rejected → pending-fulfillment → fulfilled → resolved → closed` (purchase path).

## REST API

All endpoints under `/api/tickets`, JWT Bearer auth required. Supports filtering by status, queue, assignedTo, ticketType with `limit`/`offset` pagination.

## Design Documentation

Architecture specs live in `/design`:
- `README.md` — System architecture, component diagram, technology decisions
- `ticketing-system.md` — Complete data model, API specs, UI mockups
- `async-messaging-patterns.md` — Messaging patterns, DLQ strategy, observability

## Implementation Status

Phase 2 (Core Infrastructure) is in progress. Completed: ticketing backend, REST API, Blazor UI. Pending: MCP server for agent interfaces, messaging abstraction, AI agents (Phase 3).
