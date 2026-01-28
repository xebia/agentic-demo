# GitHub Copilot Instructions

This file contains instructions and guidelines for GitHub Copilot when working with the agentic-demo project.

## Project Context

This is a demo solution based on agents, Agent-to-Agent (A2A) communication, and asynchronous messaging patterns. The project is designed for:
- Technical sales support
- Content creation (blogs, tutorials, videos)
- Establishing best practices for AI and agentic interactions
- Demonstrating messaging patterns and system integration

## Coding Guidelines

### General Principles
- Write clean, maintainable code that follows established patterns
- Prioritize readability and simplicity
- Use descriptive names for variables, functions, and classes
- Add comments only when they provide value beyond what the code expresses

### Agent Development
- Follow best practices for agent design and implementation
- Ensure agents are loosely coupled and independently deployable
- Design for asynchronous communication patterns
- Handle errors gracefully and provide meaningful feedback

### Messaging Patterns
- Use appropriate messaging patterns (pub/sub, request/reply, etc.)
- Ensure message contracts are well-defined and versioned
- Handle message failures and retries appropriately
- Log important messaging events for observability

### Documentation
- Keep documentation up-to-date with code changes
- Store design documents in the `/design` folder
- Include examples and diagrams where they add clarity
- Document architectural decisions and their rationale

## File Organization

- `/design` - Design documentation, architecture diagrams, and specifications
- Source code should be organized by domain or feature
- Tests should mirror the source code structure

## Quality Standards

- Write unit tests for business logic
- Ensure integration tests cover critical paths
- Run linters and formatters before committing
- Address security vulnerabilities promptly
