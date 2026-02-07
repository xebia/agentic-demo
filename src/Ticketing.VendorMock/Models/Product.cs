namespace Ticketing.VendorMock.Models;

public class Product
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public required string Category { get; set; }
}
