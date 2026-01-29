using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ticketing.Web.OpenApi;

/// <summary>
/// Transforms the OpenAPI document to add API metadata and JWT security scheme.
/// </summary>
public sealed class TicketingOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, 
        OpenApiDocumentTransformerContext context, 
        CancellationToken cancellationToken)
    {
        // Set API information
        document.Info.Title = "Ticketing API";
        document.Info.Version = "v1";
        document.Info.Description = """
            REST API for the Ticketing system.

            ## Authentication
            Use the `/api/auth/token` endpoint to obtain a JWT token, then include it in the Authorization header:
            ```
            Authorization: Bearer <your-token>
            ```

            ## Demo Users
            - `helpdesk-user-1` - HelpDesk role (can see all tickets)
            - `approver-1` - Approver role (can see all tickets)
            - `admin-1` - HelpDesk + Approver roles
            - `requestor-1` - Regular user (sees only own tickets)
            """;

        // Add JWT Bearer security scheme
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter your JWT token obtained from POST /api/auth/token"
        };

        return Task.CompletedTask;
    }
}
