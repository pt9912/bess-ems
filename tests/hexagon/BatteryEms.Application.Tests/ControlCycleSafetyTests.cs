using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Application.Tests;

[Trait("Category", "Safety")]
public sealed class ControlCycleSafetyTests
{
    private static (ControlCycleUseCase Cycle, InMemorySnapshotStore Snapshots, FakeClock Clock, InMemoryBatteryAssetRegistry Assets, InMemoryOperatorStopRegistry Stops)
        BuildCycle(IDispatchOptimizer? optimizer = null, IBatteryAssetRegistry? assets = null)
    {
        assets ??= new InMemoryBatteryAssetRegistry(new[] { TestFixtures.CreateAsset() });
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var stops = new InMemoryOperatorStopRegistry();
        var clock = new FakeClock();
        var cycle = new ControlCycleUseCase(
            assets,
            snapshots,
            new DefaultScheduleTracker(new InMemoryScheduleRepository()),
            stops,
            optimizer ?? new NoOpDispatchOptimizer(),
            clock,
            NoOpControlCycleMetrics.Instance,
            NullLogger<ControlCycleUseCase>.Instance,
            ControlCycleOptions.Default);
        return (cycle, snapshots, clock, (InMemoryBatteryAssetRegistry)assets, stops);
    }

    [Fact]
    public async Task Unknown_asset_yields_safe_stop()
    {
        var (cycle, _, _, _, _) = BuildCycle(assets: new InMemoryBatteryAssetRegistry());

        var cmd = await cycle.ExecuteAsync("ghost-asset", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("asset-not-registered", cmd.Reason);
    }

    [Fact]
    public async Task Missing_snapshot_yields_safe_stop()
    {
        var (cycle, _, _, _, _) = BuildCycle();

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal("no-snapshot", cmd.Reason);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
    }

    [Fact]
    public async Task Stale_snapshot_yields_safe_stop()
    {
        var (cycle, snapshots, clock, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);
        clock.UtcNow = TestFixtures.Now + TimeSpan.FromSeconds(30);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Contains("snapshot-aged", cmd.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Substituted_quality_yields_safe_stop()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(socPercent: 150), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("soc-out-of-range", cmd.Reason);
    }

    [Fact]
    public async Task Protocol_error_quality_yields_safe_stop()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(activePowerKw: double.NaN), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("active-power-not-finite", cmd.Reason);
    }

    [Fact]
    public async Task Unavailable_asset_yields_safe_stop()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(available: false), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("asset-unavailable", cmd.Reason);
    }

    [Fact]
    public async Task Invalid_optimizer_result_yields_safe_stop()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle(new BrokenOptimizer());
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("optimizer-failure", cmd.Reason);
    }

    [Fact]
    public async Task Soc_at_max_blocks_charge_request()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle(new FixedOptimizer(-25));
        snapshots.Update(TestFixtures.CreateTelemetry(socPercent: 90), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Idle, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal("soc-at-max-charge-blocked", cmd.Reason);
    }

    [Fact]
    public async Task Soc_at_min_blocks_discharge_request()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle(new FixedOptimizer(25));
        snapshots.Update(TestFixtures.CreateTelemetry(socPercent: 10), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Idle, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal("soc-at-min-discharge-blocked", cmd.Reason);
    }

    [Fact]
    public async Task Power_request_above_max_discharge_is_clamped()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle(new FixedOptimizer(75));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Discharge, cmd.Mode);
        Assert.Equal(50, cmd.ActivePowerKw);
        Assert.Equal("max-discharge-power", cmd.Reason);
    }

    [Fact]
    public async Task Operator_stop_short_circuits_every_other_input_into_safe_stop()
    {
        // LH-API-006: an active operator stop overrides telemetry,
        // schedule and optimiser. The cycle never consults the optimiser;
        // the resulting command carries the operator's reason (prefixed)
        // and CommandSource.Operator so observability can distinguish
        // operator-driven stops from fallback-driven stops.
        var (cycle, snapshots, _, _, stops) = BuildCycle(new FixedOptimizer(25));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);
        stops.Activate(new OperatorStopState("asset-1", "operator-shift-lead", "evac-drill", TestFixtures.Now));

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal(CommandSource.Operator, cmd.Source);
        Assert.Equal("operator-stop:evac-drill", cmd.Reason);
    }

    [Fact]
    public async Task Power_request_below_neg_max_charge_is_clamped()
    {
        var (cycle, snapshots, _, _, _) = BuildCycle(new FixedOptimizer(-75));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Charge, cmd.Mode);
        Assert.Equal(-50, cmd.ActivePowerKw);
        Assert.Equal("max-charge-power", cmd.Reason);
    }

    [Fact]
    public async Task Non_finite_dispatch_target_yields_safe_stop()
    {
        // RM-M3-05 cycle precheck: a NaN/Inf dispatch target would
        // propagate into the kernel and either trip the Constraint
        // comparisons or surface as a native non-finite status. The
        // cycle catches it BEFORE the kernel call so neither path
        // gets a chance to chain a blind fallback with the same
        // invalid value.
        var (cycle, snapshots, _, _, _) = BuildCycle(
            optimizer: new FixedOptimizer(double.NaN));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("dispatch-target-not-finite", cmd.Reason);
    }

    private sealed class BrokenOptimizer : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(DispatchResult.Invalid("broken", "optimizer-failure"));
    }

    private sealed class FixedOptimizer(double targetPowerKw) : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new DispatchResult("fixed", targetPowerKw, "fixed-target", IsValid: true));
    }
}
