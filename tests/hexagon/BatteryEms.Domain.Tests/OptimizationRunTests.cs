using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class OptimizationRunTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Optimal_run_carries_full_LH_OPT_009_payload()
    {
        var produced = new ScheduleReference("asset-1", ScheduleType.DayAhead, 5);
        var run = BuildRun(
            status: OptimizationSolverStatus.Optimal,
            producedSchedule: produced,
            inputs: new[] { new ScheduleReference("asset-1", ScheduleType.DayAhead, 4) });

        Assert.NotEqual(Guid.Empty, run.RunId);
        Assert.Equal("asset-1", run.AssetId);
        Assert.Equal("noop-solver", run.SolverName);
        Assert.Equal(OptimizationSolverStatus.Optimal, run.Status);
        Assert.Equal(TimeSpan.FromHours(24), run.Horizon);
        Assert.Equal(TimeSpan.FromHours(1), run.TimeStep);
        Assert.Equal(42.0, run.ObjectiveValue);
        Assert.Equal(produced, run.ProducedSchedule);
        Assert.Equal(4, Assert.Single(run.Inputs).Version);
        Assert.True(run.HasUsableSolution);
    }

    [Fact]
    public void Feasible_status_also_requires_a_produced_schedule()
    {
        var run = BuildRun(
            status: OptimizationSolverStatus.Feasible,
            producedSchedule: new ScheduleReference("asset-1", ScheduleType.DayAhead, 1));

        Assert.True(run.HasUsableSolution);
    }

    [Theory]
    [InlineData(OptimizationSolverStatus.Optimal)]
    [InlineData(OptimizationSolverStatus.Feasible)]
    public void Solution_bearing_status_without_produced_schedule_throws(OptimizationSolverStatus status)
    {
        Assert.Throws<ArgumentException>(() => BuildRun(status: status, omitProducedSchedule: true));
    }

    [Theory]
    [InlineData(OptimizationSolverStatus.Infeasible)]
    [InlineData(OptimizationSolverStatus.Unbounded)]
    [InlineData(OptimizationSolverStatus.TimeLimit)]
    [InlineData(OptimizationSolverStatus.IterationLimit)]
    [InlineData(OptimizationSolverStatus.Failed)]
    public void Non_solution_status_may_carry_no_produced_schedule(OptimizationSolverStatus status)
    {
        var run = BuildRun(status: status, omitProducedSchedule: true);
        Assert.Null(run.ProducedSchedule);
        Assert.False(run.HasUsableSolution);
    }

    [Fact]
    public void Empty_run_id_throws()
    {
        Assert.Throws<ArgumentException>(() => BuildRun(runId: Guid.Empty));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_objective_value_throws(double value)
    {
        Assert.Throws<ArgumentException>(() => BuildRun(objectiveValue: value));
    }

    [Fact]
    public void Zero_or_negative_time_step_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildRun(timeStep: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildRun(timeStep: TimeSpan.FromMinutes(-1)));
    }

    [Fact]
    public void Negative_solver_runtime_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildRun(solverRuntime: TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void Horizon_with_end_at_or_before_start_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            BuildRun(horizonStart: HorizonStart, horizonEnd: HorizonStart));
        Assert.Throws<ArgumentException>(() =>
            BuildRun(horizonStart: HorizonStart, horizonEnd: HorizonStart - TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Blank_required_string_fields_throw()
    {
        Assert.Throws<ArgumentException>(() => BuildRun(assetId: ""));
        Assert.Throws<ArgumentException>(() => BuildRun(solverName: ""));
        Assert.Throws<ArgumentException>(() => BuildRun(terminationReason: ""));
    }

    [Fact]
    public void Schedule_reference_with_negative_version_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleReference("asset-1", ScheduleType.DayAhead, -1).EnsureValid());
    }

    [Fact]
    public void Schedule_reference_with_blank_asset_id_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ScheduleReference("", ScheduleType.DayAhead, 0).EnsureValid());
    }

    private static OptimizationRun BuildRun(
        Guid? runId = null,
        string assetId = "asset-1",
        string solverName = "noop-solver",
        OptimizationSolverStatus status = OptimizationSolverStatus.Optimal,
        DateTimeOffset? horizonStart = null,
        DateTimeOffset? horizonEnd = null,
        TimeSpan? timeStep = null,
        double objectiveValue = 42.0,
        TimeSpan? solverRuntime = null,
        string terminationReason = "ok",
        IReadOnlyList<ScheduleReference>? inputs = null,
        ScheduleReference? producedSchedule = null,
        bool omitProducedSchedule = false)
    {
        // omitProducedSchedule lets a test force `null` past the default
        // fallback that otherwise materialises a schedule for solution-
        // bearing statuses.
        var resolvedProduced = omitProducedSchedule
            ? null
            : producedSchedule ?? (
                status is OptimizationSolverStatus.Optimal or OptimizationSolverStatus.Feasible
                    ? new ScheduleReference(assetId, ScheduleType.DayAhead, 1)
                    : null);

        return new OptimizationRun(
            runId: runId ?? Guid.NewGuid(),
            assetId: assetId,
            solverName: solverName,
            status: status,
            horizonStart: horizonStart ?? HorizonStart,
            horizonEnd: horizonEnd ?? HorizonStart + TimeSpan.FromHours(24),
            timeStep: timeStep ?? TimeSpan.FromHours(1),
            objectiveValue: objectiveValue,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: solverRuntime ?? TimeSpan.FromMilliseconds(15),
            terminationReason: terminationReason,
            createdAt: HorizonStart,
            inputs: inputs ?? Array.Empty<ScheduleReference>(),
            producedSchedule: resolvedProduced);
    }
}
