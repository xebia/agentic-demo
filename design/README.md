# Design Documentation

This folder contains design documentation for the agentic-demo project.

## Documents

- [Ticketing System](ticketing-system.md) - Ticketing system data model, APIs, and UI specifications
- [Async Messaging Patterns](async-messaging-patterns.md) - Event publishing and queue patterns

---

## System Overview

This system demonstrates a modern, agent-based approach to handling IT support tickets and purchasing requests. It combines human actors (employees, help desk staff, approvers) with intelligent agents (ticketing triage, purchasing policy enforcement, fulfillment) to create an efficient, scalable support and procurement system.

## Technology Stack

| Component | Technology |
|-----------|------------|
| **Agent Hosting** | Azure Function Apps (queue-triggered) |
| **Messaging** | Azure Service Bus (abstracted for RabbitMQ portability) |
| **Database** | Azure SQL Server |
| **AI Models** | Microsoft Foundry |
| **Agent Framework** | Microsoft Agent Framework (C#) |
| **Help Desk UI** | Blazor Web App (SSR + InteractiveServer) |
| **Approver UI** | Blazor Web App (SSR + InteractiveServer) |
| **Employee Chat UI** | Blazor Web App (simple chatbot interface) |

---

## System Components

### Human Actors

#### Employee
- **Role**: End user who initiates tickets and monitors their status
- **Capabilities**:
  - File new tickets (support requests, purchasing requests, etc.)
  - Check ticket status and updates
  - Receive notifications on ticket progress
- **Interface**: Blazor Web App (chatbot interface)

#### Help Desk
- **Role**: Human support staff handling IT support tickets
- **Capabilities**:
  - View assigned ticket backlog
  - Process and resolve support tickets
  - Coordinate hardware delivery and setup
  - Close resolved tickets
- **Interface**: Blazor Web App (work backlog dashboard)

#### Approver
- **Role**: Human decision-maker for escalated purchasing requests
- **Capabilities**:
  - Review escalated purchase requests that exceed policy thresholds
  - Approve or reject high-value or exceptional purchases
  - Add approval notes and conditions
- **Interface**: Blazor Web App (approval queue dashboard)

### Intelligent Agents

#### Ticketing Triage Agent
- **Role**: Automated ticket categorization and prioritization
- **Capabilities**:
  - Analyze incoming tickets using natural language processing
  - Assign appropriate tags/categories (hardware, software, access, purchasing, etc.)
  - Determine severity/priority levels
  - Route tickets to appropriate queues (help desk vs. purchasing)
- **Implementation**: C# using Microsoft Agent Framework
- **Hosting**: Azure Function App (Service Bus triggered)
- **Integration**: MCP server for ticketing system access

#### Purchasing Agent
- **Role**: Automated purchasing policy enforcement
- **Capabilities**:
  - Evaluate purchase requests against policy rules:
    - Budget thresholds
    - Approved vendor lists
    - Department spending limits
    - Item category restrictions
  - Auto-approve requests meeting all policy criteria
  - Escalate requests requiring human approval
  - Generate purchase orders for approved items
- **Implementation**: C# using Microsoft Agent Framework
- **Hosting**: Azure Function App (Service Bus triggered)
- **Integration**: MCP server for ticketing system access

#### Fulfillment Agent
- **Role**: Order processing and delivery coordination
- **Capabilities**:
  - Process approved purchase orders
  - Track order status with vendors
  - Simulate fulfillment workflow (order → shipping → delivery)
  - Create follow-up tickets for help desk when items are ready
  - Link related tickets (original purchase request + delivery task)
  - Close purchasing tickets upon successful delivery
- **Implementation**: C# using Microsoft Agent Framework
- **Hosting**: Azure Function App (Service Bus triggered)
- **Integration**: MCP server for ticketing system access

---

## Architecture

### Core Principles

1. **Abstraction**: Messaging platform is abstracted to support multiple implementations (Azure Service Bus / RabbitMQ)
2. **Flexibility**: Agents can be implemented in different languages (C#, Python, etc.)
3. **Separation of Concerns**: Agents interact via MCP server; humans via REST API
4. **Async Communication**: All agent interactions use asynchronous messaging
5. **A2A Protocol**: Agents communicate using standardized Agent-to-Agent protocols

### Key Design Decisions

#### Why Abstract the Messaging Platform?
- **Flexibility**: Support both cloud (Azure Service Bus) and on-premises (RabbitMQ) deployments
- **Testability**: Easy to mock for unit/integration tests
- **Migration**: Can switch platforms without rewriting agents

#### Why Separate MCP Server and REST API?
- **Optimization**: MCP server can be optimized for agent-to-agent communication patterns
- **Security**: Different authentication and authorization models for agents vs. humans
- **Scalability**: Can scale agent and human interfaces independently

#### Why Microsoft Agent Framework?
- **A2A Support**: Built-in Agent-to-Agent protocol implementation
- **Integration**: Works well with Azure ecosystem
- **Maturity**: Production-ready framework with good documentation
- **Standards**: Follows emerging agent communication standards

#### Why C# for Agent Implementation?
- **Framework Support**: Microsoft Agent Framework is C#-based
- **Type Safety**: Strong typing reduces runtime errors
- **Performance**: Excellent performance for real-time agent communication
- **Ecosystem**: Rich .NET ecosystem for integration tasks

#### Why Blazor for UIs?
- **Consistency**: Same stack across all three UIs enables shared components
- **SSR + InteractiveServer**: Good performance without WebAssembly complexity
- **C# throughout**: Full-stack C# development

### Ticketing System

#### MCP (Model Context Protocol) Server
- **Purpose**: Provides agent access to ticketing system
- **Consumers**: All intelligent agents
- **Operations**:
  - Create tickets
  - Update ticket status
  - Add tags/categories
  - Link related tickets
  - Query ticket information
  - Subscribe to ticket events

#### REST API
- **Purpose**: Provides human interface access to ticketing system
- **Consumers**: Blazor Web Apps (Help Desk, Approver, Employee Chat)
- **Operations**:
  - CRUD operations on tickets
  - Query tickets by status, assignee, category
  - Update workflow states
  - Retrieve ticket history and audit logs
  - Real-time updates (via SignalR)

---

## System Workflows

### Support Ticket Workflow

```
1. Employee creates support ticket via chat UI
   ↓
2. Ticketing Triage Agent:
   - Categorizes ticket (hardware/software/access)
   - Assigns severity/priority
   - Routes to Help Desk queue
   ↓
3. Help Desk receives ticket in backlog
   ↓
4. Help Desk staff resolves issue
   ↓
5. Ticket closed, Employee notified
```

### Purchase Request Workflow

```
1. Employee creates purchase request via chat UI
   ↓
2. Ticketing Triage Agent:
   - Categorizes as "purchasing"
   - Routes to Purchasing queue
   ↓
3. Purchasing Agent evaluates request:
   
   If approved by policy:
   ↓
   4a. Auto-approve and route to Fulfillment Agent
   
   If escalation needed:
   ↓
   4b. Route to Approver queue
       ↓
   5b. Approver reviews and decides
       ↓
   6b. If approved, route to Fulfillment Agent
   
   ↓
7. Fulfillment Agent:
   - Processes order
   - Tracks shipment
   - Creates delivery ticket for Help Desk
   - Links tickets (purchase + delivery)
   ↓
8. Help Desk coordinates hardware delivery
   ↓
9. Both tickets closed, Employee notified
```

---

## Inter-Component Communication

### Agent-to-Agent (A2A)
- **Protocol**: Microsoft Agent Framework A2A protocol
- **Transport**: Azure Service Bus (abstracted via `IMessageBus`)
- **Message Types**:
  - Ticket created events
  - Ticket updated events
  - Workflow state transitions
  - Agent task requests
  - Agent task responses

### Human-to-System
- **Protocol**: REST API (HTTP/JSON)
- **Authentication**: OAuth 2.0 / JWT tokens
- **Real-time Updates**: SignalR

### Agent-to-Ticketing System
- **Protocol**: MCP (Model Context Protocol)
- **Transport**: HTTP
- **Operations**: Ticket CRUD, queries, subscriptions

---

## Data Model (Conceptual)

### Ticket
- ID (unique identifier)
- Type (support, purchasing, delivery, etc.)
- Status (new, in-progress, escalated, approved, fulfilled, closed)
- Priority (low, medium, high, critical)
- Category/Tags (hardware, software, access, networking, etc.)
- Creator (Employee ID)
- Assignee (Help Desk staff ID, Approver ID, or Agent ID)
- Description
- Created/Updated timestamps
- Linked Tickets (parent/child relationships)
- History/Audit log

### Purchase Request (extends Ticket)
- Item description
- Estimated cost
- Vendor
- Justification
- Policy evaluation result
- Approval chain

See [ticketing-system.md](ticketing-system.md) for detailed data model.

---

## Implementation Phases

### Phase 1: Foundation (Design Only - Current Phase)
- Document system architecture ✓
- Define component interactions ✓
- Specify API contracts ✓
- Design data models ✓

### Phase 2: Core Infrastructure
- Implement ticketing system backend (Azure SQL)
- Create MCP server
- Develop REST API
- Set up messaging abstraction layer (Azure Service Bus)

### Phase 3: Agent Development
- Implement Ticketing Triage Agent (C#, Azure Function)
- Implement Purchasing Agent (C#, Azure Function)
- Implement Fulfillment Agent (C#, Azure Function)
- Configure A2A communication

### Phase 4: Human Interfaces
- Build Employee Chat UI (Blazor)
- Create Help Desk dashboard (Blazor)
- Develop Approver queue interface (Blazor)

### Phase 5: Integration & Testing
- End-to-end workflow testing
- Performance optimization
- Deployment automation

---

## Future Considerations

- **Multi-tenancy**: Support for multiple organizations/departments
- **Analytics**: Dashboard for ticket metrics and agent performance
- **Machine Learning**: Improve triage accuracy over time
- **External Integrations**: Connect to actual procurement systems, inventory management
- **Mobile Support**: Native mobile apps for employees and help desk
- **Internationalization**: Support for multiple languages
- **Compliance**: Audit logging and regulatory compliance features

---

## Contributing

When adding new design documentation:
1. Use clear and concise language
2. Include diagrams where they add value
3. Keep documents up-to-date as the system evolves
4. Reference related documents when relevant
