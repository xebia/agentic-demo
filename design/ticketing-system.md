# Ticketing System Design

## Overview

This document describes the minimal ticketing system required to support the agentic-demo workflows. The ticketing system is not the focus of the demo but serves as a foundational component that enables the agent workflows, messaging patterns, and human-in-the-loop interactions.

### Design Philosophy

- **Minimal Viable Design**: Include only fields and features necessary to support the demo workflows
- **Event-Driven**: All significant ticket state changes publish events for agent consumption
- **Dual Access Patterns**: MCP server for agents, REST API for human UIs
- **Related Tickets**: Support parent-child ticket relationships for workflow orchestration

## Table of Contents

- [Data Model](#data-model)
- [API Design](#api-design)
- [Event Model](#event-model)
- [Integration Points](#integration-points)
- [Help Desk UI](#help-desk-ui)
- [Approver UI](#approver-ui)
- [Workflow Examples](#workflow-examples)
- [Implementation Considerations](#implementation-considerations)

---

## Data Model

### Ticket Table

The ticket table contains the minimum fields necessary to support the demo workflows.

```sql
CREATE TABLE Tickets (
    -- Identity
    TicketId            VARCHAR(20) PRIMARY KEY,        -- e.g., "TKT-12345"
    
    -- Basic Information
    Title               NVARCHAR(200) NOT NULL,
    Description         NVARCHAR(MAX),
    
    -- Classification
    TicketType          VARCHAR(50) NOT NULL,           -- 'support', 'purchase', 'delivery'
    Category            VARCHAR(50),                    -- 'hardware', 'software', 'access', etc.
    Priority            VARCHAR(20) DEFAULT 'medium',   -- 'low', 'medium', 'high', 'critical'
    
    -- Assignment & Routing
    Status              VARCHAR(30) NOT NULL,           -- See Status Values below
    AssignedQueue       VARCHAR(50),                    -- 'helpdesk', 'purchasing', 'fulfillment'
    AssignedTo          VARCHAR(100),                   -- Specific user or agent assignment
    
    -- People
    CreatedBy           VARCHAR(100) NOT NULL,          -- Requestor email/ID
    CreatedByName       NVARCHAR(100),                  -- Requestor display name
    
    -- Timestamps
    CreatedAt           DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ClosedAt            DATETIME2,
    
    -- Workflow
    TriageDecision      VARCHAR(50),                    -- 'helpdesk', 'purchasing', 'auto-resolved'
    TriageNotes         NVARCHAR(500),                  -- Agent's triage reasoning
    ResolutionNotes     NVARCHAR(MAX),                  -- Notes when closing ticket
    
    -- Related Tickets
    ParentTicketId      VARCHAR(20),                    -- Reference to parent ticket
    
    -- Indexes
    INDEX IX_Tickets_Status (Status),
    INDEX IX_Tickets_AssignedQueue (AssignedQueue, Status),
    INDEX IX_Tickets_ParentTicketId (ParentTicketId),
    INDEX IX_Tickets_CreatedBy (CreatedBy),
    
    -- Constraints
    CONSTRAINT FK_Tickets_Parent FOREIGN KEY (ParentTicketId) 
        REFERENCES Tickets(TicketId)
);
```

### Status Values

| Status | Description | Valid Queues |
|--------|-------------|--------------|
| `new` | Ticket just created, awaiting triage | None |
| `triaged` | Triage complete, assigned to queue | Any |
| `in-progress` | Actively being worked | Any |
| `pending-approval` | Awaiting manager/budget approval | purchasing |
| `approved` | Approval granted, ready for next step | purchasing |
| `rejected` | Request denied | purchasing |
| `pending-fulfillment` | Waiting for vendor/delivery | purchasing, fulfillment |
| `fulfilled` | Item received/delivered | purchasing, fulfillment |
| `resolved` | Work complete, pending closure | Any |
| `closed` | Ticket fully closed | Any |

### Status Transition Diagram

```
                                    ┌──────────────┐
                                    │     new      │
                                    └──────┬───────┘
                                           │ Triage Agent
                                           ▼
                                    ┌──────────────┐
                                    │   triaged    │
                                    └──────┬───────┘
                           ┌───────────────┼───────────────┐
                           ▼               ▼               ▼
                    ┌──────────────┐┌──────────────┐┌──────────────┐
                    │ in-progress  ││pending-      ││auto-resolved │
                    │ (helpdesk)   ││approval      ││              │
                    └──────┬───────┘└──────┬───────┘└──────┬───────┘
                           │               │               │
                           │        ┌──────┴──────┐        │
                           │        ▼             ▼        │
                           │ ┌──────────┐ ┌──────────┐     │
                           │ │ approved │ │ rejected │     │
                           │ └────┬─────┘ └────┬─────┘     │
                           │      ▼            │           │
                           │ ┌──────────────┐  │           │
                           │ │  pending-    │  │           │
                           │ │  fulfillment │  │           │
                           │ └──────┬───────┘  │           │
                           │        ▼          │           │
                           │ ┌──────────────┐  │           │
                           │ │  fulfilled   │  │           │
                           │ └──────┬───────┘  │           │
                           ▼        ▼          ▼           ▼
                    ┌────────────────────────────────────────┐
                    │               resolved                  │
                    └────────────────────┬───────────────────┘
                                         ▼
                                  ┌──────────────┐
                                  │    closed    │
                                  └──────────────┘
```

### Related Tickets

Tickets can have parent-child relationships to support complex workflows like purchase fulfillment.

**Example Scenario**: Laptop Purchase Workflow
1. User creates ticket: "Request new laptop" → `TKT-001` (parent)
2. Triage routes to purchasing
3. Purchasing creates PO, vendor fulfills
4. When laptop arrives, fulfillment creates child ticket: "Deliver laptop to user" → `TKT-002` (child of TKT-001)
5. Help desk delivers laptop, closes `TKT-002`
6. Triage agent detects child closure, auto-closes `TKT-001`

```
Parent Ticket (TKT-001)          Child Ticket (TKT-002)
├─ Type: purchase                ├─ Type: delivery
├─ Queue: purchasing             ├─ Queue: helpdesk
├─ Status: pending-fulfillment   ├─ Status: in-progress
└─ ParentTicketId: null          └─ ParentTicketId: TKT-001
```

### Ticket History Table (Optional)

For audit trail and workflow debugging:

```sql
CREATE TABLE TicketHistory (
    HistoryId           BIGINT IDENTITY PRIMARY KEY,
    TicketId            VARCHAR(20) NOT NULL,
    FieldName           VARCHAR(50) NOT NULL,
    OldValue            NVARCHAR(MAX),
    NewValue            NVARCHAR(MAX),
    ChangedBy           VARCHAR(100) NOT NULL,
    ChangedAt           DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ChangeReason        NVARCHAR(200),
    
    INDEX IX_TicketHistory_TicketId (TicketId, ChangedAt),
    
    CONSTRAINT FK_TicketHistory_Ticket FOREIGN KEY (TicketId)
        REFERENCES Tickets(TicketId)
);
```

---

## API Design

The ticketing system exposes two API surfaces:
1. **MCP Server**: For AI agents (Triage, Purchasing, Fulfillment)
2. **REST API**: For human UIs (Help Desk, Approver)

### MCP Server Tools

The MCP server exposes the following tools for agent consumption:

#### `ticket_create`

Create a new ticket in the system.

**Parameters**:
```json
{
  "title": "string (required) - Brief description of the request",
  "description": "string (optional) - Detailed description",
  "ticketType": "string (required) - 'support' | 'purchase' | 'delivery'",
  "category": "string (optional) - 'hardware' | 'software' | 'access' | etc.",
  "priority": "string (optional) - 'low' | 'medium' | 'high' | 'critical'",
  "createdBy": "string (required) - Requestor email/ID",
  "createdByName": "string (optional) - Requestor display name",
  "parentTicketId": "string (optional) - Parent ticket for related tickets"
}
```

**Returns**:
```json
{
  "ticketId": "TKT-12345",
  "status": "new",
  "createdAt": "2026-01-28T22:00:00Z"
}
```

**Events Published**: `ticket.created`

---

#### `ticket_get`

Retrieve a ticket by ID.

**Parameters**:
```json
{
  "ticketId": "string (required) - The ticket ID to retrieve"
}
```

**Returns**: Full ticket object with all fields.

---

#### `ticket_update`

Update one or more fields on a ticket.

**Parameters**:
```json
{
  "ticketId": "string (required) - The ticket ID to update",
  "updates": {
    "status": "string (optional)",
    "assignedQueue": "string (optional)",
    "assignedTo": "string (optional)",
    "priority": "string (optional)",
    "triageDecision": "string (optional)",
    "triageNotes": "string (optional)",
    "resolutionNotes": "string (optional)"
  },
  "changedBy": "string (required) - Who is making the change",
  "changeReason": "string (optional) - Why the change was made"
}
```

**Returns**: Updated ticket object.

**Events Published**: `ticket.updated` (includes changed fields)

---

#### `ticket_assign`

Assign a ticket to a queue and optionally a specific person.

**Parameters**:
```json
{
  "ticketId": "string (required)",
  "assignedQueue": "string (required) - 'helpdesk' | 'purchasing' | 'fulfillment'",
  "assignedTo": "string (optional) - Specific person assignment",
  "triageDecision": "string (optional) - Triage routing decision",
  "triageNotes": "string (optional) - Triage reasoning",
  "changedBy": "string (required)"
}
```

**Returns**: Updated ticket object.

**Events Published**: `ticket.assigned`

---

#### `ticket_close`

Close a ticket with resolution notes.

**Parameters**:
```json
{
  "ticketId": "string (required)",
  "resolutionNotes": "string (optional) - How the ticket was resolved",
  "closedBy": "string (required)"
}
```

**Returns**: Updated ticket object with `status: 'closed'` and `closedAt` timestamp.

**Events Published**: `ticket.closed`

---

#### `ticket_list`

List tickets with optional filtering.

**Parameters**:
```json
{
  "filters": {
    "status": "string | string[] (optional)",
    "assignedQueue": "string (optional)",
    "assignedTo": "string (optional)",
    "createdBy": "string (optional)",
    "parentTicketId": "string (optional)",
    "ticketType": "string (optional)"
  },
  "includeChildren": "boolean (optional) - Include child tickets",
  "limit": "number (optional, default: 50)",
  "offset": "number (optional, default: 0)"
}
```

**Returns**: Array of ticket objects matching filters.

---

#### `ticket_get_children`

Get all child tickets for a parent ticket.

**Parameters**:
```json
{
  "parentTicketId": "string (required)"
}
```

**Returns**: Array of child ticket objects.

---

### REST API Endpoints

The REST API supports the human UIs (Help Desk and Approver).

#### Ticket Operations

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/tickets` | List tickets with query filters |
| `GET` | `/api/tickets/{ticketId}` | Get single ticket |
| `POST` | `/api/tickets` | Create new ticket |
| `PATCH` | `/api/tickets/{ticketId}` | Update ticket fields |
| `POST` | `/api/tickets/{ticketId}/close` | Close a ticket |
| `GET` | `/api/tickets/{ticketId}/children` | Get child tickets |
| `GET` | `/api/tickets/{ticketId}/history` | Get ticket history |

#### Query Parameters for `GET /api/tickets`

```
?status=in-progress,pending-approval    Comma-separated status filter
&assignedQueue=helpdesk                 Queue filter
&assignedTo=user@example.com           Assignment filter
&createdBy=user@example.com            Creator filter
&parentTicketId=TKT-001                Parent filter
&ticketType=purchase                   Type filter
&limit=50                              Page size
&offset=0                              Page offset
&sortBy=createdAt                      Sort field
&sortOrder=desc                        Sort direction
```

#### Response Format

```json
{
  "data": [...],
  "pagination": {
    "total": 150,
    "limit": 50,
    "offset": 0,
    "hasMore": true
  }
}
```

#### Error Response Format

```json
{
  "error": {
    "code": "TICKET_NOT_FOUND",
    "message": "Ticket TKT-99999 not found",
    "details": {}
  }
}
```

---

## Event Model

All significant ticket state changes publish events for consumption by agents and other systems.

### Event Structure

```json
{
  "eventId": "uuid",
  "eventType": "ticket.created",
  "timestamp": "2026-01-28T22:00:00Z",
  "correlationId": "uuid",
  "payload": {
    "ticketId": "TKT-12345",
    "ticket": { /* full ticket object */ },
    "changedBy": "triage-agent",
    "changes": { /* for update events */ }
  }
}
```

### Event Types

| Event Type | Trigger | Key Consumers |
|------------|---------|---------------|
| `ticket.created` | New ticket created | Triage Agent |
| `ticket.updated` | Any field updated | Varies by field |
| `ticket.assigned` | Queue/person assignment changed | Queue-specific agents |
| `ticket.closed` | Ticket closed | Triage Agent (for parent closure) |
| `ticket.status-changed` | Status field changed | Workflow orchestrators |

### Event Payloads

#### `ticket.created`

```json
{
  "eventType": "ticket.created",
  "payload": {
    "ticketId": "TKT-12345",
    "ticket": {
      "ticketId": "TKT-12345",
      "title": "Request new laptop",
      "ticketType": "purchase",
      "status": "new",
      "createdBy": "user@company.com",
      "createdAt": "2026-01-28T22:00:00Z"
    }
  }
}
```

#### `ticket.assigned`

```json
{
  "eventType": "ticket.assigned",
  "payload": {
    "ticketId": "TKT-12345",
    "assignedQueue": "purchasing",
    "assignedTo": null,
    "previousQueue": null,
    "triageDecision": "purchasing",
    "triageNotes": "Hardware purchase request - routing to purchasing queue",
    "changedBy": "triage-agent"
  }
}
```

#### `ticket.closed`

```json
{
  "eventType": "ticket.closed",
  "payload": {
    "ticketId": "TKT-12345",
    "parentTicketId": "TKT-00100",
    "resolutionNotes": "Laptop delivered to user at their desk",
    "closedBy": "helpdesk-user@company.com",
    "closedAt": "2026-01-28T23:30:00Z",
    "hasParent": true
  }
}
```

### Event Routing

```
Topic: tickets.events

Subscriptions:
├─ triage-agent-subscription
│   └─ Filter: eventType IN ('ticket.created', 'ticket.closed')
│
├─ purchasing-agent-subscription
│   └─ Filter: assignedQueue = 'purchasing'
│
├─ fulfillment-agent-subscription
│   └─ Filter: assignedQueue = 'fulfillment'
│
├─ helpdesk-notifications-subscription
│   └─ Filter: assignedQueue = 'helpdesk'
│
└─ analytics-subscription
    └─ Filter: none (receives all events)
```

---

## Integration Points

### Triage Agent Integration

The Triage Agent is the primary consumer of new tickets and closed child tickets.

**Subscribes to**:
- `ticket.created` - To triage new tickets
- `ticket.closed` - To check if parent tickets should be closed

**Uses MCP tools**:
- `ticket_get` - Retrieve ticket details for triage decision
- `ticket_update` - Set triage decision and notes
- `ticket_assign` - Route to appropriate queue
- `ticket_get_children` - Check child ticket status
- `ticket_close` - Close parent tickets when all children complete

**Triage Decision Flow**:
```
1. Receive ticket.created event
2. Get ticket details via ticket_get
3. Analyze ticket content (LLM-based decision)
4. Determine routing:
   - 'helpdesk' for support/access issues
   - 'purchasing' for purchase requests
   - 'auto-resolved' if information is sufficient
5. Update ticket via ticket_assign
6. Event published: ticket.assigned
```

**Parent Ticket Closure Flow**:
```
1. Receive ticket.closed event where hasParent=true
2. Get parent ticket via ticket_get
3. Get all children via ticket_get_children
4. Check if all children are closed
5. If all closed, close parent via ticket_close
6. Event published: ticket.closed (for parent)
```

### Purchasing Agent Integration

**Subscribes to**:
- `ticket.assigned` where `assignedQueue = 'purchasing'`

**Uses MCP tools**:
- `ticket_get` - Retrieve purchase request details
- `ticket_update` - Update status through approval/fulfillment workflow
- `ticket_create` - Create delivery ticket when item arrives

### Fulfillment Agent Integration

**Subscribes to**:
- `ticket.assigned` where `assignedQueue = 'fulfillment'`
- `ticket.status-changed` where `status = 'pending-fulfillment'`

**Uses MCP tools**:
- `ticket_get` - Retrieve fulfillment details
- `ticket_update` - Update fulfillment status
- `ticket_create` - Create delivery tickets for help desk

### Chat Bot Integration

The chat bot creates tickets on behalf of users.

**Uses MCP tools**:
- `ticket_create` - Create new support/purchase requests
- `ticket_get` - Check ticket status for users
- `ticket_list` - List user's open tickets

---

## Help Desk UI

The Help Desk UI provides a minimal interface for help desk personnel to manage their assigned tickets.

### Views Required

#### 1. Open Tickets View (Primary)

List of open tickets assigned to the help desk queue.

**Query**: `GET /api/tickets?assignedQueue=helpdesk&status=new,triaged,in-progress`

**Displayed Fields**:
| Field | Description |
|-------|-------------|
| Ticket ID | Link to ticket details |
| Title | Ticket title/summary |
| Priority | Visual indicator (color-coded) |
| Status | Current status badge |
| Created | Time since creation |
| Requestor | Who created the ticket |

**Actions**:
- Click ticket to view details
- Quick-close button for simple resolutions

#### 2. Closed Tickets View

Historical view of resolved help desk tickets.

**Query**: `GET /api/tickets?assignedQueue=helpdesk&status=resolved,closed`

**Purpose**: Demo visibility, audit trail, reference

#### 3. All Open Tickets View (Read-Only)

View of all open tickets across all queues.

**Query**: `GET /api/tickets?status=new,triaged,in-progress,pending-approval,approved,pending-fulfillment`

**Purpose**: Demo visibility into full system state

**Note**: Read-only view - no actions available

#### 4. Ticket Detail View

Full ticket information with action buttons.

**Displayed Fields**:
- All ticket fields
- Child tickets (if any)
- Ticket history timeline

**Actions**:
- Update status
- Add notes/comments
- Close ticket (with resolution notes)

### Ticket Close Flow

```
1. User clicks "Close Ticket" button
2. Modal appears requesting resolution notes
3. User enters resolution notes
4. UI calls: POST /api/tickets/{ticketId}/close
   Body: { "resolutionNotes": "...", "closedBy": "{current-user}" }
5. Backend updates ticket status to 'closed'
6. Backend publishes: ticket.closed event
7. UI refreshes ticket list
8. Triage agent receives event and checks for parent closure
```

### UI Mockup

```
┌────────────────────────────────────────────────────────────────────┐
│ Help Desk Dashboard                                     [User ▼]  │
├────────────────────────────────────────────────────────────────────┤
│ [Open Tickets] | [Closed Tickets] | [All Tickets (View Only)]     │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│ Open Help Desk Tickets (7)                      [Refresh] [Filter] │
│ ┌──────────────────────────────────────────────────────────────┐  │
│ │ 🔴 TKT-12350 │ Laptop delivery - John Smith        │ 2h ago │  │
│ │              │ Requestor: john.smith@company.com   │ [Close]│  │
│ ├──────────────────────────────────────────────────────────────┤  │
│ │ 🟡 TKT-12348 │ Network access request              │ 4h ago │  │
│ │              │ Requestor: jane.doe@company.com     │ [Close]│  │
│ ├──────────────────────────────────────────────────────────────┤  │
│ │ 🟢 TKT-12345 │ Password reset assistance           │ 1d ago │  │
│ │              │ Requestor: bob.jones@company.com    │ [Close]│  │
│ └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│ Showing 3 of 7 tickets                              [Load More]   │
└────────────────────────────────────────────────────────────────────┘
```

---

## Approver UI

The Approver UI provides a minimal interface for managers to approve or reject purchase requests.

### Views Required

#### 1. Pending Approvals View

List of tickets awaiting approval.

**Query**: `GET /api/tickets?status=pending-approval&assignedQueue=purchasing`

**Displayed Fields**:
| Field | Description |
|-------|-------------|
| Ticket ID | Link to ticket details |
| Title | What is being requested |
| Requestor | Who is making the request |
| Category | Type of purchase |
| Created | Time pending |

**Actions**:
- Approve request
- Reject request (with reason)

#### 2. Ticket Detail View

Full request details for informed decision.

**Displayed Fields**:
- Request details (title, description)
- Requestor information
- Category and priority
- Any attachments or additional context

**Actions**:
- Approve button
- Reject button (opens reason modal)

### Approval Flow

```
Approve:
1. User clicks "Approve" button
2. Confirmation dialog appears
3. User confirms
4. UI calls: PATCH /api/tickets/{ticketId}
   Body: { "status": "approved", "changedBy": "{current-user}" }
5. Backend updates status
6. Backend publishes: ticket.status-changed event
7. Purchasing agent receives event, proceeds with fulfillment

Reject:
1. User clicks "Reject" button
2. Modal appears requesting rejection reason
3. User enters reason and confirms
4. UI calls: PATCH /api/tickets/{ticketId}
   Body: { 
     "status": "rejected", 
     "resolutionNotes": "Rejected: {reason}",
     "changedBy": "{current-user}" 
   }
5. Backend updates status
6. Backend publishes: ticket.status-changed event
7. Notification sent to requestor
```

### UI Mockup

```
┌────────────────────────────────────────────────────────────────────┐
│ Purchase Approvals                                      [User ▼]  │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│ Pending Approval (3)                                   [Refresh]  │
│ ┌──────────────────────────────────────────────────────────────┐  │
│ │ TKT-12347 │ New MacBook Pro 16"                              │  │
│ │           │ Requestor: alice@company.com                     │  │
│ │           │ Category: Hardware | Submitted: 2 hours ago      │  │
│ │           │                           [Approve] [Reject]     │  │
│ ├──────────────────────────────────────────────────────────────┤  │
│ │ TKT-12340 │ JetBrains IDE License                            │  │
│ │           │ Requestor: charlie@company.com                   │  │
│ │           │ Category: Software | Submitted: 1 day ago        │  │
│ │           │                           [Approve] [Reject]     │  │
│ └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

---

## Workflow Examples

### Workflow 1: Simple Help Desk Request

```
User → Chat Bot → Ticketing System → Triage Agent → Help Desk UI → User

Timeline:
1. User tells chat bot: "I need help resetting my password"
2. Chat bot creates ticket via MCP: ticket_create
   → Ticket TKT-001 created, status: 'new'
   → Event: ticket.created

3. Triage Agent receives ticket.created event
4. Triage Agent analyzes ticket, decides: route to helpdesk
5. Triage Agent calls: ticket_assign(queue='helpdesk')
   → Status: 'triaged', AssignedQueue: 'helpdesk'
   → Event: ticket.assigned

6. Help Desk sees ticket in Open Tickets view
7. Help Desk assists user, clicks "Close"
8. Help Desk enters resolution notes
9. System calls: ticket_close
   → Status: 'closed'
   → Event: ticket.closed

10. User can ask chat bot for status, sees resolved
```

### Workflow 2: Purchase Request with Approval

```
User → Chat Bot → Triage → Purchasing Agent → Approver UI → Fulfillment → Help Desk

Timeline:
1. User tells chat bot: "I need a new laptop"
2. Chat bot creates ticket via MCP: ticket_create(type='purchase')
   → TKT-001 created, status: 'new'
   → Event: ticket.created

3. Triage Agent routes to purchasing
   → Status: 'triaged', Queue: 'purchasing'
   → Event: ticket.assigned

4. Purchasing Agent receives assigned event
5. Purchasing Agent determines approval needed
6. Purchasing Agent updates: status='pending-approval'
   → Event: ticket.status-changed

7. Approver sees ticket in Pending Approvals view
8. Approver clicks "Approve"
   → Status: 'approved'
   → Event: ticket.status-changed

9. Purchasing Agent creates PO, submits to vendor
10. Purchasing Agent updates: status='pending-fulfillment'
    → Event: ticket.status-changed

11. Vendor ships item, arrives at company
12. Fulfillment Agent creates delivery ticket:
    ticket_create(type='delivery', parent=TKT-001)
    → TKT-002 created, parent=TKT-001
    → Event: ticket.created

13. Triage Agent routes delivery ticket to helpdesk
    → TKT-002 Queue: 'helpdesk'
    → Event: ticket.assigned

14. Help Desk delivers laptop to user
15. Help Desk closes TKT-002
    → Event: ticket.closed (hasParent=true)

16. Triage Agent receives ticket.closed event
17. Triage Agent checks: all children of TKT-001 closed?
18. Triage Agent closes TKT-001
    → Event: ticket.closed

19. User receives notification: request fulfilled
```

### Workflow 3: Parent-Child Ticket Closure

```
Sequence Diagram:

Help Desk         Ticketing System         Triage Agent
    │                    │                      │
    │ Close TKT-002      │                      │
    │───────────────────>│                      │
    │                    │                      │
    │                    │ ticket.closed        │
    │                    │ (hasParent=true)     │
    │                    │─────────────────────>│
    │                    │                      │
    │                    │      ticket_get      │
    │                    │<─────────────────────│
    │                    │                      │
    │                    │   TKT-001 details    │
    │                    │─────────────────────>│
    │                    │                      │
    │                    │  ticket_get_children │
    │                    │<─────────────────────│
    │                    │                      │
    │                    │ [TKT-002: closed]    │
    │                    │─────────────────────>│
    │                    │                      │
    │                    │     ticket_close     │
    │                    │     (TKT-001)        │
    │                    │<─────────────────────│
    │                    │                      │
    │                    │ ticket.closed        │
    │                    │ (TKT-001)            │
    │                    │─────────────────────>│
    │                    │                      │
```

---

## Implementation Considerations

### Database Selection

For the demo, consider:
- **SQLite**: Simplest, no infrastructure needed
- **PostgreSQL**: If running in containers
- **Azure SQL**: If using Azure infrastructure
- **In-Memory**: For pure demonstration without persistence

### Ticket ID Generation

Simple sequential with prefix:

```csharp
public string GenerateTicketId()
{
    var sequence = _database.GetNextSequence("TicketSequence");
    return $"TKT-{sequence:D5}"; // TKT-00001, TKT-00002, etc.
}
```

### Event Publishing

Integrate with the messaging patterns from [async-messaging-patterns.md](async-messaging-patterns.md):

- Use the pub/sub pattern for ticket events
- Ensure idempotent event handling
- Include correlation IDs for tracing

### Authentication/Authorization

For the demo, keep it simple:
- Help Desk UI: Any authenticated user with 'helpdesk' role
- Approver UI: Any authenticated user with 'approver' role
- MCP Server: Service-to-service authentication (API key or managed identity)

### Error Handling

- Return appropriate HTTP status codes
- Include error details for debugging
- Log all errors with correlation IDs

### Demo Data

Pre-populate with sample tickets for demonstration:
- 3-5 open help desk tickets
- 1-2 pending approval tickets
- A complete purchase workflow with parent/child
- Several closed tickets for history view

---

## Open Questions

1. **Attachments**: Do we need to support file attachments on tickets?
   - *Recommendation*: No, keep minimal for demo

2. **Comments/Notes**: Should tickets have a comment thread?
   - *Recommendation*: No, use resolution notes only

3. **Email Notifications**: Should users receive email updates?
   - *Recommendation*: No, but log as if we would

4. **SLA Tracking**: Track time-to-resolution?
   - *Recommendation*: No, out of scope for demo

5. **Bulk Operations**: Close multiple tickets at once?
   - *Recommendation*: No, keep simple

---

## References

- [Async Messaging Patterns](async-messaging-patterns.md) - Event publishing and queue patterns
- [Enterprise Integration Patterns](https://www.enterpriseintegrationpatterns.com/) - Messaging patterns
- [MCP Specification](https://spec.modelcontextprotocol.io/) - Model Context Protocol

---

*Document Version: 1.0*  
*Last Updated: 2026-01-28*  
*Owner: Architecture Team*
