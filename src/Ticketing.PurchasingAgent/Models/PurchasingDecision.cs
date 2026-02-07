namespace Ticketing.PurchasingAgent.Models;

public class PurchasingDecision
{
    public required List<PurchaseItem> Items { get; set; }
    public required string Reasoning { get; set; }
    public bool AutoApproveRecommendation { get; set; }
}

public class PurchaseItem
{
    public required string Description { get; set; }
    public int Quantity { get; set; } = 1;
}
