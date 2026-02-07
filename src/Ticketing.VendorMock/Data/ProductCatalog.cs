using Ticketing.VendorMock.Models;

namespace Ticketing.VendorMock.Data;

public static class ProductCatalog
{
    public static readonly List<Product> Products =
    [
        new() { Sku = "LAP-STD", Name = "Standard Laptop", Price = 899m, Category = "Hardware" },
        new() { Sku = "LAP-DEV", Name = "Developer Laptop", Price = 1599m, Category = "Hardware" },
        new() { Sku = "LAP-EXEC", Name = "Executive Laptop", Price = 2199m, Category = "Hardware" },
        new() { Sku = "MON-24", Name = "24\" Monitor", Price = 249m, Category = "Hardware" },
        new() { Sku = "MON-27", Name = "27\" 4K Monitor", Price = 449m, Category = "Hardware" },
        new() { Sku = "MON-32", Name = "32\" Ultra-wide Monitor", Price = 649m, Category = "Hardware" },
        new() { Sku = "KEY-STD", Name = "Standard Keyboard", Price = 49m, Category = "Hardware" },
        new() { Sku = "KEY-MECH", Name = "Mechanical Keyboard", Price = 149m, Category = "Hardware" },
        new() { Sku = "MOUSE-STD", Name = "Standard Mouse", Price = 29m, Category = "Hardware" },
        new() { Sku = "MOUSE-ERG", Name = "Ergonomic Mouse", Price = 79m, Category = "Hardware" },
        new() { Sku = "DOCK-USB", Name = "USB-C Docking Station", Price = 199m, Category = "Hardware" },
        new() { Sku = "DOCK-TB", Name = "Thunderbolt Dock", Price = 329m, Category = "Hardware" },
        new() { Sku = "HEAD-STD", Name = "Standard Headset", Price = 59m, Category = "Hardware" },
        new() { Sku = "HEAD-PRO", Name = "Pro Noise-Canceling Headset", Price = 279m, Category = "Hardware" },
        new() { Sku = "CAM-HD", Name = "HD Webcam", Price = 69m, Category = "Hardware" },
        new() { Sku = "CAM-4K", Name = "4K Webcam", Price = 179m, Category = "Hardware" },
    ];

    public static List<Product> Search(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Products
            .Where(p => terms.Any(t =>
                p.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public static Product? GetBySku(string sku) =>
        Products.FirstOrDefault(p => p.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase));
}
