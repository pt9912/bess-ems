using BatteryEms.Application.Api;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class DefaultScheduleOptimizationUseCaseTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Optimal_run_is_persisted_and_schedule_replaces_repository_version()
    {
        var producedSchedule = BuildSchedule(version: 7);
        var optimizer = new SpyOptimizer(BuildResult(OptimizationSolverStatus.Optimal, producedSchedule));
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, outcome.Status);
        Assert.Equal(7, outcome.ProducedScheduleVersion);
        Assert.Same(producedSchedule, schedules.FindActive("asset-1", ScheduleType.DayAhead));
        Assert.NotNull(await runs.FindByIdAsync(outcome.RunId, CancellationToken.None));
    }

    [Fact]
    public async Task Persisted_run_is_recorded_in_metrics_for_both_optimal_and_failed_paths()
    {
        var optimalProduced = BuildSchedule(version: 1);
        var optimalRun = BuildResult(OptimizationSolverStatus.Optimal, optimalProduced).Run;
        var failedRun = BuildResult(OptimizationSolverStatus.Failed, schedule: null).Run;

        var metrics = new RecordingMetrics();
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();

        var useCaseOptimal = Build(new SpyOptimizer(new ScheduleOptimizationResult(optimalRun, optimalProduced)), schedules, runs, metrics);
        await useCaseOptimal.ExecuteAsync(BuildRequest(), CancellationToken.None);

        var useCaseFailed = Build(new SpyOptimizer(new ScheduleOptimizationResult(failedRun, null)), schedules, runs, metrics);
        await useCaseFailed.ExecuteAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(2, metrics.Recorded.Count);
        Assert.Equal(OptimizationSolverStatus.Optimal, metrics.Recorded[0].Status);
        Assert.Equal(OptimizationSolverStatus.Failed, metrics.Recorded[1].Status);
    }

    [Fact]
    public async Task Metrics_are_not_recorded_when_solver_throws()
    {
        var metrics = new RecordingMetrics();
        var useCase = Build(new SpyOptimizer(throwOnExecute: true), new InMemoryScheduleRepository(), new InMemoryOptimizationRunRepository(), metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(BuildRequest(), CancellationToken.None));

        Assert.Empty(metrics.Recorded);
    }

    [Fact]
    public async Task Infeasible_run_persists_run_but_not_schedule()
    {
        var optimizer = new SpyOptimizer(BuildResult(OptimizationSolverStatus.Infeasible, schedule: null));
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Infeasible, outcome.Status);
        Assert.Null(outcome.ProducedScheduleVersion);
        Assert.Null(schedules.FindActive("asset-1", ScheduleType.DayAhead));
        Assert.NotNull(await runs.FindByIdAsync(outcome.RunId, CancellationToken.None));
    }

    [Fact]
    public async Task Optimizer_failure_propagates_and_nothing_is_persisted()
    {
        var optimizer = new SpyOptimizer(throwOnExecute: true);
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(BuildRequest(), CancellationToken.None));

        Assert.Null(schedules.FindActive("asset-1", ScheduleType.DayAhead));
        var noRuns = await runs.QueryAsync("asset-1", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        Assert.Empty(noRuns);
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var useCase = Build(new SpyOptimizer(BuildResult()), new InMemoryScheduleRepository(), new InMemoryOptimizationRunRepository());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Outcome_carries_run_id_and_termination_reason_from_persisted_run()
    {
        var schedule = BuildSchedule(version: 3);
        var result = BuildResult(OptimizationSolverStatus.Feasible, schedule, terminationReason: "time-limit-but-feasible");
        var useCase = Build(new SpyOptimizer(result), new InMemoryScheduleRepository(), new InMemoryOptimizationRunRepository());

        var outcome = await useCase.ExecuteAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(result.Run.RunId, outcome.RunId);
        Assert.Equal("time-limit-but-feasible", outcome.TerminationReason);
        Assert.Equal(3, outcome.ProducedScheduleVersion);
    }

    private static DefaultScheduleOptimizationUseCase Build(
        IScheduleOptimizer optimizer,
        IScheduleRepository schedules,
        IOptimizationRunRepository runs,
        IOptimizationRunMetrics? metrics = null)
        => new(
            optimizer,
            schedules,
            runs,
            metrics ?? NoOpOptimizationRunMetrics.Instance,
            NullLogger<DefaultScheduleOptimizationUseCase>.Instance);

    private static ScheduleOptimizationRequest BuildRequest() => new(
        assetId: "asset-1",
        scheduleType: ScheduleType.DayAhead,
        asset: TestFixtures.CreateAsset(),
        horizonStart: HorizonStart,
        horizonEnd: HorizonStart + TimeSpan.FromHours(1),
        timeStep: TimeSpan.FromHours(1));

    private static ScheduleOptimizationResult BuildResult(
        OptimizationSolverStatus status = OptimizationSolverStatus.Optimal,
        Schedule? schedule = null,
        string terminationReason = "ok")
    {
        var hasSolution = status is OptimizationSolverStatus.Optimal or OptimizationSolverStatus.Feasible;
        var resolvedSchedule = hasSolution ? schedule ?? BuildSchedule(version: 1) : null;
        var produced = resolvedSchedule is null
            ? null
            : new ScheduleReference(resolvedSchedule.AssetId, resolvedSchedule.Type, resolvedSchedule.Version);

        var run = new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: "asset-1",
            solverName: "spy-solver",
            status: status,
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: TimeSpan.FromMilliseconds(1),
            terminationReason: terminationReason,
            createdAt: HorizonStart,
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: produced);
        return new ScheduleOptimizationResult(run, resolvedSchedule);
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

    private sealed class RecordingMetrics : IOptimizationRunMetrics
    {
        public List<OptimizationRun> Recorded { get; } = new();
        public void Record(OptimizationRun run) => Recorded.Add(run);
    }

    private sealed class SpyOptimizer : IScheduleOptimizer
    {
        private readonly ScheduleOptimizationResult? _result;
        private readonly bool _throwOnExecute;

        public SpyOptimizer(ScheduleOptimizationResult result)
        {
            _result = result;
        }

        public SpyOptimizer(bool throwOnExecute)
        {
            _throwOnExecute = throwOnExecute;
        }

        public Task<ScheduleOptimizationResult> OptimizeAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
        {
            if (_throwOnExecute)
            {
                throw new InvalidOperationException("simulated solver failure");
            }
            return Task.FromResult(_result!);
        }
    }
}
