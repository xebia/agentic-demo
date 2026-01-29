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

## CSLA .NET Development

This project uses CSLA .NET version 10 for business logic. When working with CSLA code, follow these guidelines:

### Using the CSLA MCP Server

**Always use the CSLA MCP server** when generating or modifying CSLA business objects. The MCP server provides accurate, up-to-date guidance for CSLA 10 patterns.

Available MCP tools:
- `mcp_csla-mcp_search` - Search for CSLA documentation by topic (e.g., "partial property", "editable root", "business rules")
- `mcp_csla-mcp_fetch` - Fetch specific documentation files for detailed implementation guidance
- `mcp_csla-mcp_version` - Get current CSLA version information

**Before writing CSLA code**, search the MCP server for relevant patterns:
```
mcp_csla-mcp_search("partial property implementation editable root", version=10)
mcp_csla-mcp_fetch("v10/Properties.md")
mcp_csla-mcp_fetch("v10/EditableRoot.md")
```

### CSLA 10 Modern Patterns

#### Partial Property Implementation (Required)

Use the CSLA 10 code generator with partial properties instead of verbose `RegisterProperty` patterns:

```csharp
// Modern CSLA 10 pattern (USE THIS)
[CslaImplementProperties]
public partial class Customer : BusinessBase<Customer>
{
    public partial int Id { get; private set; }
    
    [Required]
    [StringLength(100)]
    public partial string Name { get; set; }
    
    public partial DateTime CreatedAt { get; private set; }
}
```

**Do NOT use** the legacy verbose pattern:
```csharp
// Legacy pattern (DO NOT USE)
public static readonly PropertyInfo<string> NameProperty = RegisterProperty<string>(nameof(Name));
public string Name
{
    get => GetProperty(NameProperty);
    set => SetProperty(NameProperty, value);
}
```

#### Project Configuration

Business library projects must include the CSLA code generator:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Csla" Version="10.*" />
    <PackageReference Include="Csla.Generator.AutoImplementProperties.CSharp" Version="10.*" />
  </ItemGroup>
</Project>
```

#### Data Portal Operations

Use attribute-based data portal operations with dependency injection:

```csharp
[Fetch]
private async Task Fetch(int id, [Inject] ICustomerDal dal)
{
    var data = await dal.GetAsync(id);
    LoadProperty(IdProperty, data.Id);
    LoadProperty(NameProperty, data.Name);
    await BusinessRules.CheckRulesAsync();
}

[Insert]
private async Task Insert([Inject] ICustomerDal dal)
{
    var dto = new CustomerDto { Name = ReadProperty(NameProperty) };
    var result = await dal.InsertAsync(dto);
    LoadProperty(IdProperty, result.Id);
}
```

#### Business Rules via Data Annotations

Prefer data annotations over manual business rules:

```csharp
[Required]                              // Property must have a value
[StringLength(100, MinimumLength = 2)]  // String length constraints
[Range(1, 100)]                         // Numeric range
[EmailAddress]                          // Email format validation
[Display(Name = "Full Name")]           // Friendly name for UI
```

#### Read-Only Child Objects

For read-only children in lists, use `[FetchChild]` that receives data directly to avoid N+1 queries:

```csharp
[CslaImplementProperties]
public partial class OrderItemInfo : ReadOnlyBase<OrderItemInfo>
{
    public partial int Id { get; private set; }
    public partial string ProductName { get; private set; }

    [FetchChild]
    private void FetchChild(OrderItemData data)
    {
        // No database call - load from data parameter
        LoadProperty(IdProperty, data.Id);
        LoadProperty(ProductNameProperty, data.ProductName);
    }
}
```

#### List Fetching Pattern

Parent lists should fetch all child data in ONE query, then pass rows to children:

```csharp
[Fetch]
private async Task Fetch(Criteria criteria, [Inject] IDal dal, [Inject] IChildDataPortal<ItemInfo> childPortal)
{
    var items = await dal.GetAllAsync(criteria);  // ONE database call
    
    using (LoadListMode)
    {
        foreach (var data in items)
        {
            var child = await childPortal.FetchChildAsync(data);  // Pass data, no DB call
            Add(child);
        }
    }
    IsReadOnly = true;
}
```

### CSLA Object Stereotypes

Use the appropriate stereotype for each use case:
- **EditableRoot** (`BusinessBase<T>`) - Top-level editable business entity
- **EditableChild** (`BusinessBase<T>`) - Child of an editable parent
- **ReadOnlyRoot** (`ReadOnlyBase<T>`) - Top-level read-only object
- **ReadOnlyChild** (`ReadOnlyBase<T>`) - Child in a read-only parent
- **ReadOnlyRootList** (`ReadOnlyListBase<T,C>`) - List of read-only items
- **Command** (`CommandBase<T>`) - Execute operations without persistence
