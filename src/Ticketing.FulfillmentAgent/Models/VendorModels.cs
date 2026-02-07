namespace Ticketing.FulfillmentAgent.Models;

public class VendorProduct
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public required string Category { get; set; }
}

public class VendorOrderRequest
{
    public required string TicketId { get; set; }
    public required string CallbackUrl { get; set; }
    public required List<VendorOrderItem> Items { get; set; }
}

public class VendorOrderItem
{
    public required string Sku { get; set; }
    public int Quantity { get; set; } = 1;
}

public class VendorOrderResponse
{
    public required string OrderId { get; set; }
    public required string Status { get; set; }
    public required List<VendorOrderLineItem> Items { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VendorOrderLineItem
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
