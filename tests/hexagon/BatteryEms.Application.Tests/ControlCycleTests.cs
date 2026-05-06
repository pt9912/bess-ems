using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
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
            new DefaultScheduleTracker(new InMemoryScheduleRepository()),
            new InMemoryOperatorStopRegistry(),
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

    [Fact]
    public async Task Cycle_passes_active_schedule_commitments_into_dispatch_request()
    {
        // Day-Ahead schedule with a window covering TestFixtures.Now must
        // arrive at the optimizer as a Binding MarketCommitment. This is
        // RM-M1-12's "im Regelkreis verwendet" acceptance: the schedule
        // tracker bridges the repository and the dispatch boundary without
        // the cycle having to know about the storage shape.
        var assets = new InMemoryBatteryAssetRegistry(new[] { TestFixtures.CreateAsset() });
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var clock = new FakeClock();
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, new List<ScheduleWindow>
        {
            new(TestFixtures.Now - TimeSpan.FromMinutes(30), TestFixtures.Now + TimeSpan.FromMinutes(30), 12),
        });
        var repo = new InMemoryScheduleRepository(new[] { schedule });
        var optimizer = new CapturingOptimizer();
        var cycle = new ControlCycleUseCase(
            assets,
            snapshots,
            new DefaultScheduleTracker(repo),
            new InMemoryOperatorStopRegistry(),
            optimizer,
            clock,
            ControlCycleOptions.Default);
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.NotNull(optimizer.LastRequest);
        var commitment = Assert.Single(optimizer.LastRequest!.Commitments);
        Assert.Equal(MarketType.DayAhead, commitment.Market);
        Assert.Equal(12, commitment.PowerKw);
        Assert.Equal(CommitmentBindingState.Binding, commitment.BindingState);
    }

    private sealed class FixedOptimizer(double targetPowerKw) : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new DispatchResult("fixed", targetPowerKw, "fixed-target", IsValid: true));
    }

    private sealed class CapturingOptimizer : IDispatchOptimizer
    {
        public DispatchRequest? LastRequest { get; private set; }

        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new DispatchResult("captured", 0, "captured", IsValid: true));
        }
    }
}
