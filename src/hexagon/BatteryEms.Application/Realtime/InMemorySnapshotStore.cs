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

        // RM-M3-05 prereq (Managed-Precheck-Gap): non-finite SOC/SOH/
        // temperature must reach the control cycle as an unusable
        // snapshot — not as out-of-range, because a NaN never lands
        // in the [0..100] band but would silently slip past the
        // earlier `is < 0 or > 100` check (NaN comparisons are false
        // in both directions). The native kernel cannot recover from
        // non-finite inputs either, so cycling them through Constraint
        // / Ramp would just produce non-finite outputs.
        if (!double.IsFinite(telemetry.SocPercent))
        {
            return DataQuality.ProtocolError("soc-not-finite");
        }

        if (telemetry.SocPercent is < 0 or > 100)
        {
            return DataQuality.Substituted("soc-out-of-range");
        }

        if (!double.IsFinite(telemetry.SohPercent))
        {
            return DataQuality.ProtocolError("soh-not-finite");
        }

        if (telemetry.SohPercent is < 0 or > 100)
        {
            return DataQuality.Substituted("soh-out-of-range");
        }

        if (!double.IsFinite(telemetry.ActivePowerKw))
        {
            return DataQuality.ProtocolError("active-power-not-finite");
        }

        if (!double.IsFinite(telemetry.TemperatureCelsius))
        {
            return DataQuality.ProtocolError("temperature-not-finite");
        }

        return DataQuality.Valid;
    }
}
