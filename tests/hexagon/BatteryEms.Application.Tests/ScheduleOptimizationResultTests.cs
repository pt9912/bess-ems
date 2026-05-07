using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class ScheduleOptimizationResultTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Optimal_run_with_matching_produced_schedule_passes()
    {
        var schedule = BuildSchedule(version: 7);
        var run = BuildRun(
            OptimizationSolverStatus.Optimal,
            new ScheduleReference("asset-1", ScheduleType.DayAhead, 7));

        var result = new ScheduleOptimizationResult(run, schedule);
        Assert.Same(schedule, result.ProducedSchedule);
    }

    [Fact]
    public void Mismatching_version_between_run_reference_and_schedule_throws()
    {
        var schedule = BuildSchedule(version: 7);
        var run = BuildRun(
            OptimizationSolverStatus.Optimal,
            new ScheduleReference("asset-1", ScheduleType.DayAhead, 8));  // mismatch

        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationResult(run, schedule));
    }

    [Fact]
    public void Solution_status_without_schedule_throws()
    {
        var run = BuildRun(
            OptimizationSolverStatus.Optimal,
            new ScheduleReference("asset-1", ScheduleType.DayAhead, 1));
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationResult(run, producedSchedule: null));
    }

    [Fact]
    public void Non_solution_status_with_schedule_throws()
    {
        var run = BuildRun(OptimizationSolverStatus.Infeasible, producedReference: null);
        var schedule = BuildSchedule(version: 1);
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationResult(run, schedule));
    }

    [Fact]
    public void Non_solution_status_without_schedule_passes()
    {
        var run = BuildRun(OptimizationSolverStatus.Failed, producedReference: null);
        var result = new ScheduleOptimizationResult(run, producedSchedule: null);
        Assert.Null(result.ProducedSchedule);
        Assert.False(result.Run.HasUsableSolution);
    }

    private static Schedule BuildSchedule(int version) => new(
        assetId: "asset-1",
        type: ScheduleType.DayAhead,
        marketBidArea: "DE-LU",
        version: version,
        windows: new List<ScheduleWindow>
        {
            new(HorizonStart, HorizonStart + TimeSpan.FromHours(1), 0),
        });

    private static OptimizationRun BuildRun(
        OptimizationSolverStatus status,
        ScheduleReference? producedReference) => new(
            runId: Guid.NewGuid(),
            assetId: "asset-1",
            solverName: "noop-solver",
            status: status,
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: TimeSpan.FromMilliseconds(1),
            terminationCode: "ok",
            terminationDetail: null,
            createdAt: HorizonStart,
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: producedReference);
}
