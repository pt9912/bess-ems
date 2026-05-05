using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class ControlCycleTests
{
    private static (ControlCycleUseCase Cycle, InMemorySnapshotStore Snapshots, FakeClock Clock, InMemoryBatteryAssetRegistry Assets)
        BuildCycle(IDispatchOptimizer? optimizer = null)
    {
        var assets = new InMemoryBatteryAssetRegistry(new[] { TestFixtures.CreateAsset() });
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var clock = new FakeClock();
        var cycle = new ControlCycleUseCase(
            assets,
            snapshots,
            optimizer ?? new NoOpDispatchOptimizer(),
            clock,
            ControlCycleOptions.Default);
        return (cycle, snapshots, clock, assets);
    }

    [Fact]
    public async Task Happy_path_returns_idle_command_with_noop_optimizer()
    {
        var (cycle, snapshots, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal("asset-1", cmd.AssetId);
        Assert.Equal(CommandMode.Idle, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal(CommandSource.Optimization, cmd.Source);
    }

    [Fact]
    public async Task Discharge_dispatch_produces_discharge_command()
    {
        var (cycle, snapshots, _, _) = BuildCycle(new FixedOptimizer(targetPowerKw: 25));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Discharge, cmd.Mode);
        Assert.Equal(25, cmd.ActivePowerKw);
    }

    [Fact]
    public async Task Charge_dispatch_produces_charge_command()
    {
        var (cycle, snapshots, _, _) = BuildCycle(new FixedOptimizer(targetPowerKw: -25));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Charge, cmd.Mode);
        Assert.Equal(-25, cmd.ActivePowerKw);
    }

    private sealed class FixedOptimizer(double targetPowerKw) : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new DispatchResult("fixed", targetPowerKw, "fixed-target", IsValid: true));
    }
}
