using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

// RM-M2-OP-09 — replay / reproducibility test (LH-OPT-009 acceptance).
// Plan §Open RM-M2-OP-OPEN-03 made determinism a "Soll" for M2 because
// it depends on the solver backend; GLOP (the OR-Tools LP backend OP-05
// wired in) is deterministic by design — no random seed in the simplex
// — so for this adapter we *can* assert bit-exact equality on the
// dashboard-relevant fields. Fields that are non-deterministic by
// design (RunId, CreatedAt, SolverRuntime) are excluded; comparing
// those would test the runtime, not the model.
public sealed class OrToolsScheduleOptimizerReplayTests
{
    private static readonly double[] CheapThenExpensive = { 10.0, 200.0 };
    private static readonly double[] DayAheadProfile =
    {
        38.5, 35.2, 33.1, 32.0, 31.5, 33.0, 38.2, 45.7,
        58.3, 65.1, 71.4, 78.9, 82.5, 80.1, 75.6, 70.2,
        68.4, 72.0, 79.5, 92.3, 98.1, 88.4, 65.0, 47.2,
    };

    [Fact]
    public async Task Same_optimizer_instance_twice_yields_identical_solution()
    {
        // Replay-on-the-same-instance: state-free per-call solver
        // construction in OrToolsScheduleOptimizer means two back-to-back
        // calls share no implicit state.
        var optimizer = Build();
        var request = NewRequest(CheapThenExpensive);

        var first = await optimizer.OptimizeAsync(request, CancellationToken.None);
        var second = await optimizer.OptimizeAsync(request, CancellationToken.None);

        AssertSolutionsAreReplayEquivalent(first, second);
    }

    [Fact]
    public async Task Two_fresh_optimizer_instances_yield_identical_solution()
    {
        // Cross-instance determinism: a fresh process / DI scope must
        // produce the same answer the audit log captured. This is the
        // "rerun-from-stored-inputs" guarantee LH-OPT-009 cares about.
        var first = await Build().OptimizeAsync(NewRequest(CheapThenExpensive), CancellationToken.None);
        var second = await Build().OptimizeAsync(NewRequest(CheapThenExpensive), CancellationToken.None);

        AssertSolutionsAreReplayEquivalent(first, second);
    }

    [Fact]
    public async Task Day_ahead_24h_profile_replays_bit_exact_across_runs()
    {
        // Realistic M2 scenario: 24-h horizon, 1-h step, varying
        // EUR/MWh prices. If GLOP ever becomes non-deterministic on a
        // larger model the bit-exact assertion catches it before the
        // operator finds a divergent audit row.
        var request = NewRequest(DayAheadProfile);

        var first = await Build().OptimizeAsync(request, CancellationToken.None);
        var second = await Build().OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, first.Run.Status);
        AssertSolutionsAreReplayEquivalent(first, second);
        // Schedule has 24 windows — the assertion below confirms every
        // window's TargetPowerKw matches bit-exact.
        Assert.Equal(24, first.ProducedSchedule!.Windows.Count);
    }

    [Fact]
    public async Task Different_options_yield_potentially_different_solution()
    {
        // Negative control: if the configured InitialSocPercent changes,
        // the answer must change too. Otherwise the determinism test
        // above could pass by accident even if the optimiser were
        // ignoring its inputs entirely.
        var midSoc = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions { InitialSocPercent = 50 },
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance);
        var lowSoc = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions { InitialSocPercent = 15 },
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance);

        var midResult = await midSoc.OptimizeAsync(NewRequest(CheapThenExpensive), CancellationToken.None);
        var lowResult = await lowSoc.OptimizeAsync(NewRequest(CheapThenExpensive), CancellationToken.None);

        // A 15% initial SOC has less energy to discharge at the high
        // price than a 50% initial SOC, so the objective (a cost) is
        // strictly less negative.
        Assert.NotEqual(midResult.Run.ObjectiveValue, lowResult.Run.ObjectiveValue);
    }

    private static void AssertSolutionsAreReplayEquivalent(
        ScheduleOptimizationResult first,
        ScheduleOptimizationResult second)
    {
        // Status / termination — the dashboard grouping axis.
        Assert.Equal(first.Run.Status, second.Run.Status);
        Assert.Equal(first.Run.TerminationCode, second.Run.TerminationCode);
        Assert.Equal(first.Run.TerminationDetail, second.Run.TerminationDetail);

        // Objective value — bit-exact on the same machine.
        Assert.Equal(first.Run.ObjectiveValue, second.Run.ObjectiveValue);
        Assert.Equal(
            first.Run.ObjectiveBreakdown.Components.Count,
            second.Run.ObjectiveBreakdown.Components.Count);
        for (var i = 0; i < first.Run.ObjectiveBreakdown.Components.Count; i++)
        {
            Assert.Equal(
                first.Run.ObjectiveBreakdown.Components[i].Name,
                second.Run.ObjectiveBreakdown.Components[i].Name);
            Assert.Equal(
                first.Run.ObjectiveBreakdown.Components[i].Value,
                second.Run.ObjectiveBreakdown.Components[i].Value);
            Assert.Equal(
                first.Run.ObjectiveBreakdown.Components[i].Unit,
                second.Run.ObjectiveBreakdown.Components[i].Unit);
        }

        // Schedule shape and target power per step.
        if (first.ProducedSchedule is null || second.ProducedSchedule is null)
        {
            Assert.Null(first.ProducedSchedule);
            Assert.Null(second.ProducedSchedule);
            return;
        }
        Assert.Equal(first.ProducedSchedule.AssetId, second.ProducedSchedule.AssetId);
        Assert.Equal(first.ProducedSchedule.Type, second.ProducedSchedule.Type);
        Assert.Equal(first.ProducedSchedule.MarketBidArea, second.ProducedSchedule.MarketBidArea);
        Assert.Equal(first.ProducedSchedule.Version, second.ProducedSchedule.Version);
        Assert.Equal(first.ProducedSchedule.Windows.Count, second.ProducedSchedule.Windows.Count);
        for (var i = 0; i < first.ProducedSchedule.Windows.Count; i++)
        {
            var a = first.ProducedSchedule.Windows[i];
            var b = second.ProducedSchedule.Windows[i];
            Assert.Equal(a.Start, b.Start);
            Assert.Equal(a.End, b.End);
            Assert.Equal(a.TargetPowerKw, b.TargetPowerKw);
        }
    }

    private static OrToolsScheduleOptimizer Build() => new(
        new ScheduleSolverOptions(),
        new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
        NullLogger<OrToolsScheduleOptimizer>.Instance);

    private static ScheduleOptimizationRequest NewRequest(IReadOnlyList<double> prices)
    {
        var timeStep = TimeSpan.FromHours(1);
        var command = new ScheduleOptimizationCommand(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: TestFixtures.HorizonStart,
            horizonEnd: TestFixtures.HorizonStart + TimeSpan.FromTicks(timeStep.Ticks * prices.Count),
            timeStep: timeStep,
            pricesPerStep: prices,
            priceUnit: "EUR/MWh");
        return new ScheduleOptimizationRequest(command, "DE-LU", baseScheduleVersion: 0);
    }
}
