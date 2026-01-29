using System.Text;
using Csla.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Ticketing.DataAccess;
using Ticketing.DataAccess.Abstractions;
using Ticketing.DataAccess.Dal;
using Ticketing.DataAccess.Seeding;
using Ticketing.DataAccess.Services;
using Ticketing.Domain;
using Ticketing.Domain.Services;
using Ticketing.Web.Components;
using Ticketing.Web.Middleware;
using Ticketing.Web.OpenApi;
using Ticketing.Web.Services.Auth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

// Configure Entity Framework with LocalDB
builder.Services.AddDbContext<TicketingDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TicketingDb") 
        ?? "Server=(localdb)\\mssqllocaldb;Database=TicketingDb;Trusted_Connection=True;MultipleActiveResultSets=true"));

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

// Configure JWT settings for API authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.Configure<JwtSettings>(options =>
{
    options.SecretKey = jwtSettings.SecretKey;
    options.Issuer = jwtSettings.Issuer;
    options.Audience = jwtSettings.Audience;
    options.ExpirationMinutes = jwtSettings.ExpirationMinutes;
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<ApiUserContext>();

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
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
    };
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<MockAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => 
    sp.GetRequiredService<MockAuthenticationStateProvider>());
builder.Services.AddScoped<IUserContext, BlazorUserContext>();
builder.Services.AddCascadingAuthenticationState();

// Add controllers for REST API
builder.Services.AddControllers();

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
        .WithPreferredScheme("Bearer")
        .WithHttpBearerAuthentication(bearer => bearer.Token = "");
});

app.MapStaticAssets();
app.MapControllers(); // REST API endpoints
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
