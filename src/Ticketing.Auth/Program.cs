using Ticketing.Auth.Endpoints;
using Ticketing.Auth.Models;
using Ticketing.Auth.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure auth settings
builder.Services.Configure<AuthSettings>(
    builder.Configuration.GetSection("AuthSettings"));

// Configure service accounts
builder.Services.Configure<List<ServiceAccount>>(
    builder.Configuration.GetSection("ServiceAccounts"));

// Register services
builder.Services.AddSingleton<RsaKeyService>();
builder.Services.AddScoped<IdentityStore>();
builder.Services.AddScoped<TokenService>();

// Add OpenAPI support
builder.Services.AddEndpointsApiExplorer();

// Configure CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors();

// Map endpoints
app.MapTokenEndpoints();
app.MapJwksEndpoints();

// Health check
app.MapGet("/", () => Results.Ok(new
{
    service = "Ticketing.Auth",
    status = "healthy",
    endpoints = new[]
    {
        "POST /token - Get user token",
        "POST /token/client-credentials - Get service account token",
        "GET /.well-known/jwks.json - Get public keys",
        "GET /users - List available demo users"
    }
}));

app.MapDefaultEndpoints();

app.Run();
