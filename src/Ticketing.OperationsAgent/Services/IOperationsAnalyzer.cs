using Ticketing.OperationsAgent.Models;

namespace Ticketing.OperationsAgent.Services;

public interface IOperationsAnalyzer
{
    Task<List<OperationsAlert>> AnalyzeHealthScanAsync(HealthScanResult scanResult, CancellationToken ct = default);
}
