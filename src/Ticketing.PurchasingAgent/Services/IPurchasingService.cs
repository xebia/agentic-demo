using Ticketing.PurchasingAgent.Models;

namespace Ticketing.PurchasingAgent.Services;

public interface IPurchasingService
{
    Task<PurchasingDecision> AnalyzePurchaseRequestAsync(
        TicketDetailResponse ticket,
        CancellationToken cancellationToken = default);
}
