namespace Ticketing.Web.Services;

public interface IAlertService
{
    IReadOnlyList<OperationsAlertDto> Alerts { get; }
    int UnacknowledgedCount { get; }
    event Action? OnAlertsChanged;
    void AddAlert(OperationsAlertDto alert);
    void AcknowledgeAlert(string alertId);
}

public class AlertService : IAlertService
{
    private readonly Lock _lock = new();
    private readonly List<OperationsAlertDto> _alerts = [];
    private const int MaxAlerts = 100;

    public IReadOnlyList<OperationsAlertDto> Alerts
    {
        get
        {
            lock (_lock)
            {
                return _alerts.ToList().AsReadOnly();
            }
        }
    }

    public int UnacknowledgedCount
    {
        get
        {
            lock (_lock)
            {
                return _alerts.Count(a => !a.IsAcknowledged);
            }
        }
    }

    public event Action? OnAlertsChanged;

    public void AddAlert(OperationsAlertDto alert)
    {
        lock (_lock)
        {
            _alerts.Insert(0, alert);
            if (_alerts.Count > MaxAlerts)
            {
                _alerts.RemoveRange(MaxAlerts, _alerts.Count - MaxAlerts);
            }
        }
        OnAlertsChanged?.Invoke();
    }

    public void AcknowledgeAlert(string alertId)
    {
        lock (_lock)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert is not null)
            {
                alert.IsAcknowledged = true;
            }
        }
        OnAlertsChanged?.Invoke();
    }
}
