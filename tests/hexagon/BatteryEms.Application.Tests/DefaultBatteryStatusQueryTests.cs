using BatteryEms.Application.Api;
using BatteryEms.Application.Assets;
using BatteryEms.Application.IO;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class DefaultBatteryStatusQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Returns_null_when_asset_is_not_registered()
    {
        var query = new DefaultBatteryStatusQuery(
            new InMemoryBatteryAssetRegistry(),
            new InMemorySnapshotStore(TimeSpan.FromSeconds(10)),
            new InMemoryCommandRepository());

        var view = await query.FindAsync("ghost", Now, CancellationToken.None);

        Assert.Null(view);
    }

    [Fact]
    public async Task Returns_view_with_telemetry_and_command_when_known()
    {
        var asset = TestFixtures.CreateAsset();
        var assets = new InMemoryBatteryAssetRegistry(new[] { asset });
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var commands = new InMemoryCommandRepository();

        var telemetry = TestFixtures.CreateTelemetry();
        snapshots.Update(telemetry, telemetry.Timestamp);

        var command = SampleCommand(asset.AssetId);
        await commands.AppendAsync(command, CommandDispatchResult.Ok(command.Timestamp, "ok"), CancellationToken.None);

        var query = new DefaultBatteryStatusQuery(assets, snapshots, commands);
        var view = await query.FindAsync(asset.AssetId, Now, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(asset.AssetId, view!.AssetId);
        Assert.Equal(telemetry.SocPercent, view.Telemetry!.SocPercent);
        Assert.Equal(command.CommandId, view.LastCommand!.CommandId);
        Assert.Equal(telemetry.Timestamp, view.ObservedAt);
    }

    [Fact]
    public async Task Returns_view_with_null_fields_when_no_snapshot_or_command_recorded_yet()
    {
        var asset = TestFixtures.CreateAsset();
        var assets = new InMemoryBatteryAssetRegistry(new[] { asset });
        var query = new DefaultBatteryStatusQuery(
            assets,
            new InMemorySnapshotStore(TimeSpan.FromSeconds(10)),
            new InMemoryCommandRepository());

        var view = await query.FindAsync(asset.AssetId, Now, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Null(view!.Telemetry);
        Assert.Null(view.LastCommand);
        Assert.Null(view.ObservedAt);
    }

    private static BatteryCommand SampleCommand(string assetId) => new(
        CommandId: "cmd-status-1",
        Timestamp: Now,
        AssetId: assetId,
        Mode: CommandMode.Discharge,
        ActivePowerKw: 25,
        ReactivePowerKvar: 0,
        ValidUntil: Now + TimeSpan.FromSeconds(5),
        Reason: "schedule",
        Source: CommandSource.Optimization);
}
