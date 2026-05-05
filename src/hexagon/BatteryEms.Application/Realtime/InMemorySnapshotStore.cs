using System.Collections.Concurrent;
using BatteryEms.Domain;

namespace BatteryEms.Application.Realtime;

public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly TimeSpan _maxAge;
    private readonly ConcurrentDictionary<string, Snapshot> _byAsset = new(StringComparer.Ordinal);

    public InMemorySnapshotStore(TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), "Max age must be positive.");
        }

        _maxAge = maxAge;
    }

    public TimeSpan MaxAge => _maxAge;

    public void Update(BatteryTelemetry telemetry, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        var quality = Plausibilize(telemetry);
        _byAsset[telemetry.AssetId] = new Snapshot(telemetry, receivedAt, quality);
    }

    public Snapshot? GetLatest(string assetId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        if (!_byAsset.TryGetValue(assetId, out var snapshot))
        {
            return null;
        }

        var age = now - snapshot.ReceivedAt;
        if (age > _maxAge && snapshot.Quality.IsUsableForControl)
        {
            return snapshot with { Quality = DataQuality.Stale($"snapshot-aged-{age.TotalSeconds:F1}s") };
        }

        return snapshot;
    }

    private static DataQuality Plausibilize(BatteryTelemetry telemetry)
    {
        if (!telemetry.DataQuality.IsUsableForControl)
        {
            return telemetry.DataQuality;
        }

        if (telemetry.SocPercent is < 0 or > 100)
        {
            return DataQuality.Substituted("soc-out-of-range");
        }

        if (telemetry.SohPercent is < 0 or > 100)
        {
            return DataQuality.Substituted("soh-out-of-range");
        }

        if (double.IsNaN(telemetry.ActivePowerKw) || double.IsInfinity(telemetry.ActivePowerKw))
        {
            return DataQuality.ProtocolError("active-power-not-finite");
        }

        return DataQuality.Valid;
    }
}
