namespace Ticketing.PurchasingAgent.Models;

public class QuoteRequest
{
    public required List<string> Items { get; set; }
    public required string TicketId { get; set; }
}

public class QuoteLineItem
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public bool Available { get; set; }
}

public class QuoteResponse
{
    public required List<QuoteLineItem> LineItems { get; set; }
    public decimal TotalEstimate { get; set; }
    public bool Available { get; set; }
}
