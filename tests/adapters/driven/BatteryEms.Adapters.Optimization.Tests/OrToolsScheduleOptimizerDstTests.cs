using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

// RM-M2-02 / LH-MKT-007 — Optimization-Pipeline-Test über die DST-
// Spring-Forward-Grenze. Acceptance: "Tests decken mindestens eine
// Sommerzeitumstellung ab; Fahrplanimport, Persistenz und Regelkreis
// interpretieren Zeitintervalle identisch." Schedule itself has a
// matching test in Domain (ScheduleTests); this layer covers the
// Optimization-Pipeline-Seite: Horizon spannt UTC-23:00 vor der
// Wall-Clock-Spring-Forward-Grenze über den DST-Sprung hinweg, das
// LP-Modell läuft ohne Sonderbehandlung von Sommer-/Winterzeit, weil
// die Speicherung intern UTC ist und der Schritt in UTC-Tick-Algebra
// erfolgt.
public sealed class OrToolsScheduleOptimizerDstTests
{
    // 2026-03-29 02:00 CET → 03:00 CEST (Europe/Berlin). In UTC the
    // boundary is 01:00:00Z; the LP starts at 22:00Z the day before so
    // a 6-hour horizon in 1 h steps spans the wall-clock jump.
    private static readonly DateTimeOffset DstHorizonStart =
        new(2026, 3, 28, 22, 0, 0, TimeSpan.Zero);

    private static readonly double[] FlatSixHour = { 50.0, 50.0, 50.0, 50.0, 50.0, 50.0 };
    private static readonly double[] LowHighProfile = { 30.0, 25.0, 20.0, 80.0, 90.0, 100.0 };

    [Fact]
    public async Task Six_hour_horizon_across_dst_spring_forward_produces_six_steps()
    {
        // Six hourly windows over the DST boundary stay six windows in
        // UTC. The schedule the LP emits has continuous half-open
        // [Start, End) windows with no gap and no overlap at the jump.
        var optimizer = Build();
        var request = NewDstRequest(prices: FlatSixHour);

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal(6, result.ProducedSchedule!.Windows.Count);

        // Continuity in UTC across the wall-clock jump: window i ends
        // exactly where window i+1 starts, with one-hour spacing.
        for (var i = 0; i < result.ProducedSchedule.Windows.Count - 1; i++)
        {
            var current = result.ProducedSchedule.Windows[i];
            var next = result.ProducedSchedule.Windows[i + 1];
            Assert.Equal(current.End, next.Start);
            Assert.Equal(TimeSpan.FromHours(1), current.Duration);
        }
        Assert.Equal(TimeSpan.FromHours(1), result.ProducedSchedule.Windows[^1].Duration);

        // Horizon spans 6 hours start-to-end regardless of the wall-clock
        // jump in the middle.
        Assert.Equal(TimeSpan.FromHours(6), result.Run.HorizonEnd - result.Run.HorizonStart);
    }

    [Fact]
    public async Task Optimization_across_dst_is_bit_exactly_deterministic()
    {
        // Pair the existing replay precedent (RM-M2-OP-09) with the DST
        // boundary: same inputs produce bit-exact identical outputs even
        // when the horizon crosses a wall-clock jump. Catches a future
        // refactor that incorrectly applies a local-time conversion
        // anywhere inside the LP build.
        var optimizer = Build();
        var first = await optimizer.OptimizeAsync(NewDstRequest(LowHighProfile), CancellationToken.None);
        var second = await optimizer.OptimizeAsync(NewDstRequest(LowHighProfile), CancellationToken.None);

        Assert.Equal(first.Run.ObjectiveValue, second.Run.ObjectiveValue);
        Assert.NotNull(first.ProducedSchedule);
        Assert.NotNull(second.ProducedSchedule);
        Assert.Equal(first.ProducedSchedule!.Windows.Count, second.ProducedSchedule!.Windows.Count);
        for (var i = 0; i < first.ProducedSchedule.Windows.Count; i++)
        {
            var a = first.ProducedSchedule.Windows[i];
            var b = second.ProducedSchedule.Windows[i];
            Assert.Equal(a.Start, b.Start);
            Assert.Equal(a.End, b.End);
            Assert.Equal(a.TargetPowerKw, b.TargetPowerKw);
        }
    }

    [Fact]
    public async Task Schedule_windows_remain_in_utc_across_dst_jump()
    {
        // The LP must never emit local-time offsets — Schedule consumers
        // (loader, persistence, IScheduleTracker) all assume Offset ==
        // Zero. A future regression that pulled local-time into a
        // window's Start/End would break the half-open interval algebra
        // around the jump.
        var optimizer = Build();
        var result = await optimizer.OptimizeAsync(NewDstRequest(FlatSixHour), CancellationToken.None);

        Assert.NotNull(result.ProducedSchedule);
        foreach (var window in result.ProducedSchedule!.Windows)
        {
            Assert.Equal(TimeSpan.Zero, window.Start.Offset);
            Assert.Equal(TimeSpan.Zero, window.End.Offset);
        }
    }

    private static OrToolsScheduleOptimizer Build() => new(
        new ScheduleSolverOptions(),
        new TestFixtures.FrozenClock(DstHorizonStart),
        NullLogger<OrToolsScheduleOptimizer>.Instance);

    private static ScheduleOptimizationRequest NewDstRequest(IReadOnlyList<double> prices)
    {
        var timeStep = TimeSpan.FromHours(1);
        var command = new ScheduleOptimizationCommand(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: DstHorizonStart,
            horizonEnd: DstHorizonStart + TimeSpan.FromTicks(timeStep.Ticks * prices.Count),
            timeStep: timeStep,
            pricesPerStep: prices,
            priceUnit: "EUR/MWh");
        return new ScheduleOptimizationRequest(command, "DE-LU", baseScheduleVersion: 0);
    }
}
