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

    private static readonly int?[] ExpectedSerialisedVersions = { 1, 2 };
    private static readonly int[] ExpectedSerialisedBaseVersions = { 0, 1 };

    [Fact]
    public async Task Optimal_run_is_persisted_and_schedule_replaces_repository_version()
    {
        // No prior schedule → BaseVersion=0 → optimiser produces v1.
        var optimizer = new SpyOptimizer(req => BuildResult(
            req, OptimizationSolverStatus.Optimal, includeSchedule: true));
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildInputs(), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, outcome.Status);
        Assert.Equal(1, outcome.ProducedScheduleVersion);
        var stored = schedules.FindActive("asset-1", ScheduleType.DayAhead);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.Version);
        Assert.Equal("DE-LU", stored.MarketBidArea);
        Assert.NotNull(await runs.FindByIdAsync(outcome.RunId, CancellationToken.None));
    }

    [Fact]
    public async Task Use_case_inherits_market_bid_area_and_bumps_version_when_prior_schedule_exists()
    {
        var schedules = new InMemoryScheduleRepository();
        // Seed v3 with a non-default bid area; the use case must derive
        // the next request's identity from this row, not the M2 default.
        schedules.Replace(BuildSchedule(version: 3, marketBidArea: "AT"));

        var optimizer = new SpyOptimizer(req => BuildResult(
            req, OptimizationSolverStatus.Optimal, includeSchedule: true));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildInputs(), CancellationToken.None);

        Assert.Equal(4, outcome.ProducedScheduleVersion);
        Assert.Equal("AT", optimizer.LastRequest!.MarketBidArea);
        Assert.Equal(3, optimizer.LastRequest.BaseScheduleVersion);
    }

    [Fact]
    public async Task Concurrent_calls_for_same_asset_type_are_serialised_per_key()
    {
        // Without the per-(asset, type) lock the second call would read
        // the same v0 the first started from and produce a clashing v1.
        // SpyOptimizer with a delay forces overlap if the lock isn't
        // applied; we then assert that the use case observed v1 and v2
        // sequentially, not two clashing v1s (review #1).
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();
        var observedBaseVersions = new System.Collections.Concurrent.ConcurrentBag<int>();

        var optimizer = new SpyOptimizer(req =>
        {
            observedBaseVersions.Add(req.BaseScheduleVersion);
            // Yield so two queued calls have an opportunity to interleave
            // if the lock is missing — the lock keeps them serial.
            Thread.Sleep(20);
            return BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true);
        });
        var useCase = Build(optimizer, schedules, runs);

        var inputs = BuildInputs();
        var first = useCase.ExecuteAsync(inputs, CancellationToken.None);
        var second = useCase.ExecuteAsync(inputs, CancellationToken.None);
        await Task.WhenAll(first, second);

        var outcomes = new[] { await first, await second };
        // Exactly one of them produced version 1, the other version 2.
        var versions = outcomes.Select(o => o.ProducedScheduleVersion).OrderBy(v => v).ToArray();
        Assert.Equal(ExpectedSerialisedVersions, versions);
        // Optimiser saw BaseVersion 0 then 1 — never two BaseVersion=0s.
        var orderedBases = observedBaseVersions.OrderBy(v => v).ToArray();
        Assert.Equal(ExpectedSerialisedBaseVersions, orderedBases);
    }

    [Fact]
    public async Task Persisted_run_is_recorded_in_metrics_for_both_optimal_and_failed_paths()
    {
        var metrics = new RecordingMetrics();
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();

        var optimalCase = Build(
            new SpyOptimizer(req => BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true)),
            schedules, runs, metrics);
        await optimalCase.ExecuteAsync(BuildInputs(), CancellationToken.None);

        var failedCase = Build(
            new SpyOptimizer(req => BuildResult(req, OptimizationSolverStatus.Failed, includeSchedule: false)),
            schedules, runs, metrics);
        await failedCase.ExecuteAsync(BuildInputs(), CancellationToken.None);

        Assert.Equal(2, metrics.Recorded.Count);
        Assert.Equal(OptimizationSolverStatus.Optimal, metrics.Recorded[0].Status);
        Assert.Equal(OptimizationSolverStatus.Failed, metrics.Recorded[1].Status);
    }

    [Fact]
    public async Task Metrics_are_not_recorded_when_solver_throws()
    {
        var metrics = new RecordingMetrics();
        var useCase = Build(
            new SpyOptimizer(throwOnExecute: true),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository(),
            metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(BuildInputs(), CancellationToken.None));

        Assert.Empty(metrics.Recorded);
    }

    [Fact]
    public async Task Infeasible_run_persists_run_but_not_schedule()
    {
        var optimizer = new SpyOptimizer(req =>
            BuildResult(req, OptimizationSolverStatus.Infeasible, includeSchedule: false));
        var schedules = new InMemoryScheduleRepository();
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildInputs(), CancellationToken.None);

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
            useCase.ExecuteAsync(BuildInputs(), CancellationToken.None));

        Assert.Null(schedules.FindActive("asset-1", ScheduleType.DayAhead));
        var noRuns = await runs.QueryAsync("asset-1", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        Assert.Empty(noRuns);
    }

    [Fact]
    public async Task Null_inputs_throws()
    {
        var useCase = Build(
            new SpyOptimizer(req => BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true)),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Outcome_carries_run_id_and_termination_reason_from_persisted_run()
    {
        var optimizer = new SpyOptimizer(req =>
            BuildResult(req, OptimizationSolverStatus.Feasible, includeSchedule: true,
                terminationCode: "time-limit-but-feasible"));
        var useCase = Build(optimizer, new InMemoryScheduleRepository(), new InMemoryOptimizationRunRepository());

        var outcome = await useCase.ExecuteAsync(BuildInputs(), CancellationToken.None);

        Assert.Equal("time-limit-but-feasible", outcome.TerminationReason);
        Assert.Equal(1, outcome.ProducedScheduleVersion);
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

    private static ScheduleOptimizationInputs BuildInputs() => new(
        assetId: "asset-1",
        scheduleType: ScheduleType.DayAhead,
        asset: TestFixtures.CreateAsset(),
        horizonStart: HorizonStart,
        horizonEnd: HorizonStart + TimeSpan.FromHours(1),
        timeStep: TimeSpan.FromHours(1));

    // The optimiser must produce a Schedule whose version matches
    // request.BaseScheduleVersion + 1; the use case enforces this at
    // ScheduleOptimizationResult construction time.
    private static ScheduleOptimizationResult BuildResult(
        ScheduleOptimizationRequest request,
        OptimizationSolverStatus status,
        bool includeSchedule,
        string terminationCode = "ok",
        string? terminationDetail = null)
    {
        Schedule? schedule = null;
        ScheduleReference? produced = null;
        if (includeSchedule)
        {
            var producedVersion = request.BaseScheduleVersion + 1;
            schedule = BuildSchedule(version: producedVersion, marketBidArea: request.MarketBidArea);
            produced = new ScheduleReference(schedule.AssetId, schedule.Type, schedule.Version);
        }

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
            terminationCode: terminationCode,
            terminationDetail: terminationDetail,
            createdAt: HorizonStart,
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: produced);
        return new ScheduleOptimizationResult(run, schedule);
    }

    private static Schedule BuildSchedule(int version, string marketBidArea) => new(
        assetId: "asset-1",
        type: ScheduleType.DayAhead,
        marketBidArea: marketBidArea,
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
        private readonly Func<ScheduleOptimizationRequest, ScheduleOptimizationResult>? _resultFactory;
        private readonly bool _throwOnExecute;

        public ScheduleOptimizationRequest? LastRequest { get; private set; }

        public SpyOptimizer(Func<ScheduleOptimizationRequest, ScheduleOptimizationResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public SpyOptimizer(bool throwOnExecute)
        {
            _throwOnExecute = throwOnExecute;
        }

        public Task<ScheduleOptimizationResult> OptimizeAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (_throwOnExecute)
            {
                throw new InvalidOperationException("simulated solver failure");
            }
            return Task.FromResult(_resultFactory!(request));
        }
    }
}
