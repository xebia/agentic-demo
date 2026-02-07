namespace Ticketing.VendorMock.Models;

public class OrderRequest
{
    public required string TicketId { get; set; }
    public required string CallbackUrl { get; set; }
    public required List<OrderRequestItem> Items { get; set; }
}

public class OrderRequestItem
{
    public required string Sku { get; set; }
    public int Quantity { get; set; } = 1;
}

public class OrderResponse
{
    public required string OrderId { get; set; }
    public required string Status { get; set; }
    public required List<OrderLineItem> Items { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VendorCallbackPayload
{
    public required string OrderId { get; set; }
    public required string TicketId { get; set; }
    public required string Status { get; set; }
    public required string Message { get; set; }
    public required List<CallbackItem> Items { get; set; }
}

public class CallbackItem
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public int Quantity { get; set; }
}
