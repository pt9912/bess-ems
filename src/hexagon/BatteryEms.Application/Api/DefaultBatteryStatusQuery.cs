using BatteryEms.Application.Assets;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;

namespace BatteryEms.Application.Api;

public sealed class DefaultBatteryStatusQuery : IBatteryStatusQuery
{
    private readonly IBatteryAssetRegistry _assets;
    private readonly ISnapshotStore _snapshots;
    private readonly ICommandRepository _commands;

    public DefaultBatteryStatusQuery(
        IBatteryAssetRegistry assets,
        ISnapshotStore snapshots,
        ICommandRepository commands)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(commands);
        _assets = assets;
        _snapshots = snapshots;
        _commands = commands;
    }

    public async Task<BatteryStatusView?> FindAsync(string assetId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        // Asset must be registered for the API to surface anything; an
        // unknown asset is a 404 at the boundary, not an empty status.
        if (_assets.Find(assetId) is null)
        {
            return null;
        }

        var snapshot = _snapshots.GetLatest(assetId, now);
        var command = await _commands.FindLatestAsync(assetId, cancellationToken).ConfigureAwait(false);

        return new BatteryStatusView(
            AssetId: assetId,
            Telemetry: snapshot?.Telemetry,
            Quality: snapshot?.Quality,
            ObservedAt: snapshot?.ReceivedAt,
            LastCommand: command);
    }
}
