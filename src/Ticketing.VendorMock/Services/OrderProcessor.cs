using System.Collections.Concurrent;
using System.Net.Http.Json;
using Ticketing.VendorMock.Models;

namespace Ticketing.VendorMock.Services;

/// <summary>
/// Background service that processes pending orders after a simulated delay.
/// 80% chance of delivery, 20% chance of unfulfillable.
/// </summary>
public class OrderProcessor : BackgroundService
{
    private static readonly string[] UnfulfillableReasons =
    [
        "Item out of stock",
        "Item discontinued by manufacturer",
        "Supply chain delay — item unavailable",
        "Vendor allocation exhausted for this quarter"
    ];

    internal static readonly ConcurrentDictionary<string, Order> Orders = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(IHttpClientFactory httpClientFactory, ILogger<OrderProcessor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static OrderResponse SubmitOrder(OrderRequest request)
    {
        var orderId = $"ORD-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var items = request.Items.Select(i =>
        {
            var product = Data.ProductCatalog.GetBySku(i.Sku);
            return new OrderLineItem
            {
                Sku = i.Sku,
                Name = product?.Name ?? i.Sku,
                Quantity = i.Quantity,
                UnitPrice = product?.Price ?? 0
            };
        }).ToList();

        var order = new Order
        {
            OrderId = orderId,
            TicketId = request.TicketId,
            CallbackUrl = request.CallbackUrl,
            Items = items,
            ProcessAfter = DateTime.UtcNow.AddSeconds(Random.Shared.Next(15, 31))
        };

        Orders[orderId] = order;

        return new OrderResponse
        {
            OrderId = orderId,
            Status = order.Status,
            Items = items,
            Total = items.Sum(i => i.UnitPrice * i.Quantity),
            CreatedAt = order.CreatedAt
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderProcessor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            var pendingOrders = Orders.Values
                .Where(o => o.Status == "Pending" && o.ProcessAfter <= DateTime.UtcNow)
                .ToList();

            foreach (var order in pendingOrders)
            {
                try
                {
                    await ProcessOrderAsync(order, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process order {OrderId}", order.OrderId);
                }
            }
        }
    }

    private async Task ProcessOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var delivered = Random.Shared.NextDouble() < 0.8;

        if (delivered)
        {
            order.Status = "Delivered";
            _logger.LogInformation("Order {OrderId} delivered for ticket {TicketId}", order.OrderId, order.TicketId);
        }
        else
        {
            order.Status = "Unfulfillable";
            _logger.LogInformation("Order {OrderId} unfulfillable for ticket {TicketId}", order.OrderId, order.TicketId);
        }

        var callback = new VendorCallbackPayload
        {
            OrderId = order.OrderId,
            TicketId = order.TicketId,
            Status = delivered ? "delivered" : "unfulfillable",
            Message = delivered
                ? "Order delivered successfully"
                : UnfulfillableReasons[Random.Shared.Next(UnfulfillableReasons.Length)],
            Items = order.Items.Select(i => new CallbackItem
            {
                Sku = i.Sku,
                Name = i.Name,
                Quantity = i.Quantity
            }).ToList()
        };

        try
        {
            var client = _httpClientFactory.CreateClient("CallbackClient");
            var response = await client.PostAsJsonAsync(order.CallbackUrl, callback, cancellationToken);
            _logger.LogInformation(
                "Callback sent for order {OrderId}: {StatusCode}",
                order.OrderId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send callback for order {OrderId} to {CallbackUrl}",
                order.OrderId, order.CallbackUrl);
        }
    }
}
