using BatteryEms.Application.Observability;

namespace BatteryEms.Application.Tests;

// Hand-rolled spy — keeps the Observability tests self-contained
// without pulling in NSubstitute for trivial recording-only mocks.
internal sealed class SpyControlCycleMetrics : IControlCycleMetrics
{
    public List<(string AssetId, TimeSpan Duration)> CycleDurations { get; } = new();
    public List<(string AssetId, string Reason)> InvalidSnapshots { get; } = new();
    public List<(string AssetId, string Component)> CommunicationErrors { get; } = new();
    public List<(string AssetId, TimeSpan Latency)> CommandLatencies { get; } = new();
    public Dictionary<string, double> LatestActivePowerKw { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> LatestSocPercent { get; } = new(StringComparer.Ordinal);
    public List<(string AssetId, string Reason)> SafeStops { get; } = new();

    public void RecordCycleDuration(string assetId, TimeSpan duration) => CycleDurations.Add((assetId, duration));
    public void IncrementInvalidSnapshot(string assetId, string reason) => InvalidSnapshots.Add((assetId, reason));
    public void IncrementCommunicationError(string assetId, string component) => CommunicationErrors.Add((assetId, component));
    public void RecordCommandLatency(string assetId, TimeSpan latency) => CommandLatencies.Add((assetId, latency));
    public void SetActivePowerKw(string assetId, double valueKw) => LatestActivePowerKw[assetId] = valueKw;
    public void SetSocPercent(string assetId, double valuePercent) => LatestSocPercent[assetId] = valuePercent;
    public void RecordSafeStop(string assetId, string reason) => SafeStops.Add((assetId, reason));
}
