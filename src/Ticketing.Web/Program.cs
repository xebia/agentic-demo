using Csla.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using Scalar.AspNetCore;
using Ticketing.DataAccess;
using Ticketing.DataAccess.Abstractions;
using Ticketing.DataAccess.Dal;
using Ticketing.DataAccess.Seeding;
using Ticketing.DataAccess.Services;
using Ticketing.Domain;
using Ticketing.Domain.Services;
using Ticketing.Messaging.ServiceBus;
using Ticketing.Web.Components;
using Ticketing.Web.Mcp;
using Ticketing.Web.Middleware;
using Ticketing.Web.OpenApi;
using Ticketing.Web.Services;
using Ticketing.Web.Services.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

// Configure Entity Framework
builder.Services.AddDbContext<TicketingDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TicketingDb")
        ?? "Server=(localdb)\\mssqllocaldb;Database=TicketingDb;Trusted_Connection=True;MultipleActiveResultSets=true"));

builder.EnrichSqlServerDbContext<TicketingDbContext>();

// Register DAL services
builder.Services.AddScoped<ITicketIdGenerator, TicketIdGenerator>();
builder.Services.AddScoped<ITicketListDal, TicketListDal>();
builder.Services.AddScoped<ITicketEditDal, TicketEditDal>();
builder.Services.AddScoped<ITicketHistoryDal, TicketHistoryDal>();

// Configure CSLA for Blazor Server
builder.Services.AddCsla(options => options
    .AddAspNetCore()
    .AddServerSideBlazor(blazor => blazor
        .UseInMemoryApplicationContextManager = true));

// Configure JWT settings for API authentication via auth service
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.AddScoped<ApiUserContext>();

// Register auth service client for fetching users from central auth service
builder.Services.AddHttpClient<AuthServiceClient>();

// Configure authentication with both Cookie (for Blazor) and JWT Bearer (for API)
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "MockAuth";
    options.DefaultChallengeScheme = "MockAuth";
})
.AddCookie("MockAuth", options =>
{
    options.LoginPath = "/"; // No real login - just redirect to home
})
.AddJwtBearer("Bearer", options =>
{
    // Configure JWKS retrieval from auth service
    var jwksUrl = $"{jwtSettings.AuthServiceUrl}/.well-known/jwks.json";
    options.ConfigurationManager = new JwksConfigurationManager(jwksUrl);

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience
    };
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<MockAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => 
    sp.GetRequiredService<MockAuthenticationStateProvider>());
builder.Services.AddScoped<IUserContext, BlazorUserContext>();
builder.Services.AddCascadingAuthenticationState();

// Add operations alert service and chaos health check
builder.Services.AddSingleton<IAlertService, AlertService>();
builder.Services.AddHealthChecks()
    .AddCheck("chaos-degradation", () =>
        ChaosState.IsHealthDegraded
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("Chaos testing: service deliberately degraded")
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// Add event publishing via Azure Service Bus
builder.Services.AddServiceBusMessaging(builder.Configuration);

// Add controllers for REST API
builder.Services.AddControllers();

// Configure MCP Server for LLM agent access
// MCP tools allow LLMs to act on behalf of authenticated users
builder.Services.AddScoped<McpUserContext>();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<TicketingTools>()
    .WithTools<FulfillmentTools>();

// Configure OpenAPI documentation
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<TicketingOpenApiTransformer>();
});

var app = builder.Build();

// Apply migrations and seed data in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
    await context.Database.MigrateAsync();
    await DemoDataSeeder.SeedAsync(context);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAutoSignIn();
app.UseAuthorization();
app.UseAntiforgery();

// Map OpenAPI and Scalar documentation endpoints
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Ticketing API")
        .WithTheme(ScalarTheme.BluePlanet)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .AddPreferredSecuritySchemes("Bearer")
        .AddHttpAuthentication("Bearer", auth => auth.Token = "");
});

app.MapStaticAssets();
app.MapControllers(); // REST API endpoints

// Map MCP endpoint at /mcp path
// Requires JWT Bearer authentication - LLMs must obtain a token from auth service first
app.MapMcp("/mcp")
    .RequireAuthorization(policy => policy
        .AddAuthenticationSchemes("Bearer")
        .RequireAuthenticatedUser());

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
