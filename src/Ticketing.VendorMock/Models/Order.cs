namespace Ticketing.VendorMock.Models;

public class Order
{
    public required string OrderId { get; set; }
    public required string TicketId { get; set; }
    public required string CallbackUrl { get; set; }
    public required List<OrderLineItem> Items { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessAfter { get; set; }
}

public class OrderLineItem
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}
