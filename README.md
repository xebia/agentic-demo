# Agentic Demo - IT Support & Purchasing System

Demo solution based on agents, Agent-to-Agent (A2A) communication, and async messaging for IT support and purchasing workflows.

## Overview

This system demonstrates a modern, agent-based approach to handling IT support tickets and purchasing requests. It combines human actors (employees, help desk staff, approvers) with intelligent agents (ticketing triage, purchasing policy enforcement, fulfillment) to create an efficient, scalable support and procurement system.

## System Components

### Human Actors

#### Employee
- **Role**: End user who initiates tickets and monitors their status
- **Capabilities**:
  - File new tickets (support requests, purchasing requests, etc.)
  - Check ticket status and updates
  - Receive notifications on ticket progress
- **Interface**: Web portal or mobile app (REST API consumer)

#### Help Desk
- **Role**: Human support staff handling IT support tickets
- **Capabilities**:
  - View assigned ticket backlog
  - Process and resolve support tickets
  - Coordinate hardware delivery and setup
  - Close resolved tickets
- **Interface**: Work backlog dashboard (REST API consumer)

#### Approver
- **Role**: Human decision-maker for escalated purchasing requests
- **Capabilities**:
  - Review escalated purchase requests that exceed policy thresholds
  - Approve or reject high-value or exceptional purchases
  - Add approval notes and conditions
- **Interface**: Approval queue dashboard (REST API consumer)

### Intelligent Agents

#### Ticketing Triage Agent
- **Role**: Automated ticket categorization and prioritization
- **Capabilities**:
  - Analyze incoming tickets using natural language processing
  - Assign appropriate tags/categories (hardware, software, access, purchasing, etc.)
  - Determine severity/priority levels
  - Route tickets to appropriate queues (help desk vs. purchasing)
- **Implementation**: C# using Microsoft Agent Framework
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
- **Integration**: MCP server for ticketing system access

## Architecture

### Core Principles

1. **Abstraction**: Messaging platform is abstracted to support multiple implementations
2. **Flexibility**: Agents can be implemented in different languages (C#, Python, etc.)
3. **Separation of Concerns**: Agents interact via MCP server; humans via REST API
4. **Async Communication**: All agent interactions use asynchronous messaging
5. **A2A Protocol**: Agents communicate using standardized Agent-to-Agent protocols

### Technology Stack

#### Messaging Platform (Abstract)
- **Options**: 
  - RabbitMQ (open-source message broker)
  - Azure Service Bus (cloud-native messaging)
- **Design**: Abstract interface allows switching between implementations
- **Purpose**: Decouples agents and enables asynchronous communication

#### Agent Implementation
- **Primary Language**: C# 
- **Framework**: Microsoft Agent Framework (for A2A support)
- **Alternative**: Python (for specific agents as needed)
- **Runtime**: .NET (for C# agents)

#### Ticketing System

##### MCP (Model Context Protocol) Server
- **Purpose**: Provides agent access to ticketing system
- **Consumers**: All intelligent agents
- **Operations**:
  - Create tickets
  - Update ticket status
  - Add tags/categories
  - Link related tickets
  - Query ticket information
  - Subscribe to ticket events

##### REST API
- **Purpose**: Provides human interface access to ticketing system
- **Consumers**: Web portals, dashboards, mobile apps
- **Operations**:
  - CRUD operations on tickets
  - Query tickets by status, assignee, category
  - Update workflow states
  - Retrieve ticket history and audit logs
  - Real-time updates (via webhooks or SSE)

## System Workflows

### Support Ticket Workflow

```
1. Employee creates support ticket
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
1. Employee creates purchase request ticket
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

## Inter-Component Communication

### Agent-to-Agent (A2A)
- **Protocol**: Microsoft Agent Framework A2A protocol
- **Transport**: Async messaging (RabbitMQ/Azure Service Bus)
- **Message Types**:
  - Ticket created events
  - Ticket updated events
  - Workflow state transitions
  - Agent task requests
  - Agent task responses

### Human-to-System
- **Protocol**: REST API (HTTP/JSON)
- **Authentication**: OAuth 2.0 / JWT tokens
- **Real-time Updates**: WebSockets or Server-Sent Events

### Agent-to-Ticketing System
- **Protocol**: MCP (Model Context Protocol)
- **Transport**: HTTP or local IPC
- **Operations**: Ticket CRUD, queries, subscriptions

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

## Implementation Phases

### Phase 1: Foundation (Design Only - Current Phase)
- Document system architecture ✓
- Define component interactions ✓
- Specify API contracts (future)
- Design data models (future)

### Phase 2: Core Infrastructure (Future)
- Implement ticketing system backend
- Create MCP server
- Develop REST API
- Set up messaging abstraction layer

### Phase 3: Agent Development (Future)
- Implement Ticketing Triage Agent (C#)
- Implement Purchasing Agent (C#)
- Implement Fulfillment Agent (C#)
- Configure A2A communication

### Phase 4: Human Interfaces (Future)
- Build Employee portal
- Create Help Desk dashboard
- Develop Approver queue interface

### Phase 5: Integration & Testing (Future)
- End-to-end workflow testing
- Performance optimization
- Deployment automation

## Design Decisions

### Why Abstract the Messaging Platform?
- **Flexibility**: Support both cloud (Azure Service Bus) and on-premises (RabbitMQ) deployments
- **Testability**: Easy to mock for unit/integration tests
- **Migration**: Can switch platforms without rewriting agents

### Why Separate MCP Server and REST API?
- **Optimization**: MCP server can be optimized for agent-to-agent communication patterns
- **Security**: Different authentication and authorization models for agents vs. humans
- **Scalability**: Can scale agent and human interfaces independently

### Why Microsoft Agent Framework?
- **A2A Support**: Built-in Agent-to-Agent protocol implementation
- **Integration**: Works well with Azure ecosystem
- **Maturity**: Production-ready framework with good documentation
- **Standards**: Follows emerging agent communication standards

### Why C# for Initial Implementation?
- **Framework Support**: Microsoft Agent Framework is C#-based
- **Type Safety**: Strong typing reduces runtime errors
- **Performance**: Excellent performance for real-time agent communication
- **Ecosystem**: Rich .NET ecosystem for integration tasks

## Future Considerations

- **Multi-tenancy**: Support for multiple organizations/departments
- **Analytics**: Dashboard for ticket metrics and agent performance
- **Machine Learning**: Improve triage accuracy over time
- **External Integrations**: Connect to actual procurement systems, inventory management
- **Mobile Support**: Native mobile apps for employees and help desk
- **Internationalization**: Support for multiple languages
- **Compliance**: Audit logging and regulatory compliance features

## Getting Started

(To be added in future phases)

## Contributing

(To be added in future phases)

## License

See [LICENSE](LICENSE) file for details.
