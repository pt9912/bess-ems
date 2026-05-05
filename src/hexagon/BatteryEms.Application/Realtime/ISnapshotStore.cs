using BatteryEms.Domain;

namespace BatteryEms.Application.Realtime;

public interface ISnapshotStore
{
    void Update(BatteryTelemetry telemetry, DateTimeOffset receivedAt);

    Snapshot? GetLatest(string assetId, DateTimeOffset now);
}
