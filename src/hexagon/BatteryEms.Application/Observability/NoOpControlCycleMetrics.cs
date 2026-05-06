namespace BatteryEms.Application.Observability;

// Default sink used when no telemetry adapter is wired. Lets headless
// hosts (tests, dry runs) drive the regulation cycle without forcing a
// Prometheus dependency or null-checks at every call site.
public sealed class NoOpControlCycleMetrics : IControlCycleMetrics
{
    public static readonly NoOpControlCycleMetrics Instance = new();

    public void RecordCycleDuration(string assetId, TimeSpan duration) { }
    public void IncrementInvalidSnapshot(string assetId, string reason) { }
    public void IncrementCommunicationError(string assetId, string component) { }
    public void RecordCommandLatency(string assetId, TimeSpan latency) { }
    public void SetActivePowerKw(string assetId, double valueKw) { }
    public void SetSocPercent(string assetId, double valuePercent) { }
    public void RecordSafeStop(string assetId, string reason) { }
}
