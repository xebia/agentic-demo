using Ticketing.VendorMock.Data;
using Ticketing.VendorMock.Models;
using Ticketing.VendorMock.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("CallbackClient")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

builder.Services.AddHostedService<OrderProcessor>();

var app = builder.Build();

app.MapDefaultEndpoints();

// GET /api/catalog/search?query={query}
app.MapGet("/api/catalog/search", (string query) =>
{
    var results = ProductCatalog.Search(query);
    return Results.Ok(results);
});

// GET /api/catalog/products
app.MapGet("/api/catalog/products", () => Results.Ok(ProductCatalog.Products));

// GET /api/catalog/products/{sku}
app.MapGet("/api/catalog/products/{sku}", (string sku) =>
{
    var product = ProductCatalog.GetBySku(sku);
    return product is not null ? Results.Ok(product) : Results.NotFound();
});

// POST /api/orders
app.MapPost("/api/orders", (OrderRequest request) =>
{
    var response = OrderProcessor.SubmitOrder(request);
    return Results.Created($"/api/orders/{response.OrderId}", response);
});

// GET /api/orders/{orderId}
app.MapGet("/api/orders/{orderId}", (string orderId) =>
{
    if (OrderProcessor.Orders.TryGetValue(orderId, out var order))
    {
        return Results.Ok(new OrderResponse
        {
            OrderId = order.OrderId,
            Status = order.Status,
            Items = order.Items,
            Total = order.Items.Sum(i => i.UnitPrice * i.Quantity),
            CreatedAt = order.CreatedAt
        });
    }
    return Results.NotFound();
});

app.Run();
