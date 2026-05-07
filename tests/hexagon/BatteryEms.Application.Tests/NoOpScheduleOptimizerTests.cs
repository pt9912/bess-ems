// Regression cover for the M2 stub used until OR-Tools lands (RM-M2-OP-05).
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class NoOpScheduleOptimizerTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OptimizeAsync_returns_failed_run_with_no_solver_configured_reason()
    {
        var optimizer = new NoOpScheduleOptimizer(new FakeClock { UtcNow = HorizonStart });
        var request = new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0);

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Equal("no-solver-configured", result.Run.TerminationReason);
        Assert.Null(result.ProducedSchedule);
        Assert.False(result.Run.HasUsableSolution);
    }

    [Fact]
    public async Task OptimizeAsync_throws_for_null_request()
    {
        var optimizer = new NoOpScheduleOptimizer(new FakeClock());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            optimizer.OptimizeAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_throws_for_null_clock()
    {
        Assert.Throws<ArgumentNullException>(() => new NoOpScheduleOptimizer(null!));
    }
}
