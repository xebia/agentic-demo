using Ticketing.Chatbot.Components;
using Ticketing.Chatbot.Models;
using Ticketing.Chatbot.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure chat settings
builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("ChatSettings"));

// Register services
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddHttpClient<AuthService>();
builder.Services.AddHttpClient<McpClientService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
