using BatteryEms.Application.Api;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Time;
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

        var outcome = await useCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

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
        schedules.Replace(BuildSchedule(version: 3, marketBidArea: "AT"), expectedBaseVersion: 0);

        var optimizer = new SpyOptimizer(req => BuildResult(
            req, OptimizationSolverStatus.Optimal, includeSchedule: true));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

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

        var command = BuildCommand();
        var first = useCase.ExecuteAsync(command, CancellationToken.None);
        var second = useCase.ExecuteAsync(command, CancellationToken.None);
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
        await optimalCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

        var failedCase = Build(
            new SpyOptimizer(req => BuildResult(req, OptimizationSolverStatus.Failed, includeSchedule: false)),
            schedules, runs, metrics);
        await failedCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

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
            useCase.ExecuteAsync(BuildCommand(), CancellationToken.None));

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

        var outcome = await useCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

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
            useCase.ExecuteAsync(BuildCommand(), CancellationToken.None));

        Assert.Null(schedules.FindActive("asset-1", ScheduleType.DayAhead));
        var noRuns = await runs.QueryAsync("asset-1", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        Assert.Empty(noRuns);
    }

    [Fact]
    public async Task Null_command_throws()
    {
        var useCase = Build(
            new SpyOptimizer(req => BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true)),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Dispose_makes_subsequent_execute_throw_object_disposed()
    {
        // Review C2: the use case owns native SemaphoreSlim handles in
        // _locks; Dispose releases them and rejects further work.
        var useCase = Build(
            new SpyOptimizer(req => BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true)),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository());
        await useCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

        useCase.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            useCase.ExecuteAsync(BuildCommand(), CancellationToken.None));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var useCase = Build(
            new SpyOptimizer(req => BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true)),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository());

        useCase.Dispose();
        useCase.Dispose(); // must not throw under repeated calls
    }

    [Fact]
    public async Task Cas_conflict_on_replace_persists_failed_run_with_concurrent_version_conflict_code()
    {
        // Sibling-replica race: we read v3, optimise to v4, but a sibling
        // already advanced the store to v4. Our Replace must fail with
        // ScheduleConcurrencyConflictException and the use case must
        // turn that into a Failed run.
        var schedules = new ConflictingScheduleRepository(
            seedActualVersion: 3,
            actualVersionAtReplace: 4);
        var optimizer = new SpyOptimizer(req =>
            BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true));
        var runs = new InMemoryOptimizationRunRepository();
        // Clock is well after HorizonStart so the createdAt assertion
        // can distinguish "clock-sourced" from "originalRun-sourced".
        var clock = new FakeClock { UtcNow = HorizonStart + TimeSpan.FromHours(2) };
        var useCase = Build(optimizer, schedules, runs, clock: clock);

        var outcome = await useCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Null(outcome.ProducedScheduleVersion);
        Assert.Equal("concurrent-version-conflict:expected=3,actual=4", outcome.TerminationReason);

        var stored = await runs.FindByIdAsync(outcome.RunId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(OptimizationSolverStatus.Failed, stored!.Status);
        Assert.Equal("concurrent-version-conflict", stored.TerminationCode);
        Assert.Equal("expected=3,actual=4", stored.TerminationDetail);
        Assert.Null(stored.ProducedSchedule);
        // SolverName attributes the failure to the persistence-side CAS
        // guard, not to the optimiser (review m-2). Dashboards that group
        // by SolverName must not mis-count this as a solver failure.
        Assert.Equal("schedule-cas-guard", stored.SolverName);
        // CreatedAt is sourced from the clock injected into the use case
        // (review m-3), not from the original Optimal run. A future
        // refactor that wires originalRun.CreatedAt through would flip
        // this assertion.
        Assert.Equal(clock.UtcNow, stored.CreatedAt);
    }

    [Fact]
    public async Task Use_case_replaces_with_existing_version_not_produced_version()
    {
        // Wiring pin: the use case must call Replace with the version
        // it READ (existing.Version), not the version the optimiser
        // PRODUCED (existing.Version + 1). The latter would be a no-op
        // CAS that defeats RM-M3-FUP-02 entirely.
        var schedules = new RecordingScheduleRepository(seedVersion: 5);
        var optimizer = new SpyOptimizer(req =>
            BuildResult(req, OptimizationSolverStatus.Optimal, includeSchedule: true));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        await useCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

        var call = Assert.Single(schedules.ReplaceCalls);
        Assert.Equal(5, call.ExpectedBaseVersion);
        Assert.Equal(6, call.SchedulePassedIn.Version);
    }

    private sealed class ConflictingScheduleRepository : IScheduleRepository
    {
        private readonly Schedule? _seed;
        private readonly int _actualVersionAtReplace;

        public ConflictingScheduleRepository(int seedActualVersion, int actualVersionAtReplace)
        {
            _actualVersionAtReplace = actualVersionAtReplace;
            _seed = seedActualVersion > 0
                ? BuildSchedule(version: seedActualVersion, marketBidArea: "DE-LU")
                : null;
        }

        public IEnumerable<Schedule> FindAll(string assetId) =>
            _seed is not null ? new[] { _seed } : Array.Empty<Schedule>();

        public Schedule? FindActive(string assetId, ScheduleType type) => _seed;

        public void Replace(Schedule schedule, int expectedBaseVersion) =>
            throw new ScheduleConcurrencyConflictException(
                schedule.AssetId, schedule.Type, expectedBaseVersion, _actualVersionAtReplace);
    }

    private sealed class RecordingScheduleRepository : IScheduleRepository
    {
        private readonly Schedule? _seed;
        public List<(int ExpectedBaseVersion, Schedule SchedulePassedIn)> ReplaceCalls { get; } = new();

        public RecordingScheduleRepository(int seedVersion)
        {
            _seed = seedVersion > 0
                ? BuildSchedule(version: seedVersion, marketBidArea: "DE-LU")
                : null;
        }

        public IEnumerable<Schedule> FindAll(string assetId) =>
            _seed is not null ? new[] { _seed } : Array.Empty<Schedule>();

        public Schedule? FindActive(string assetId, ScheduleType type) => _seed;

        public void Replace(Schedule schedule, int expectedBaseVersion)
        {
            ReplaceCalls.Add((expectedBaseVersion, schedule));
        }
    }

    [Fact]
    public async Task Outcome_carries_run_id_and_termination_reason_from_persisted_run()
    {
        var optimizer = new SpyOptimizer(req =>
            BuildResult(req, OptimizationSolverStatus.Feasible, includeSchedule: true,
                terminationCode: "time-limit-but-feasible"));
        var useCase = Build(optimizer, new InMemoryScheduleRepository(), new InMemoryOptimizationRunRepository());

        var outcome = await useCase.ExecuteAsync(BuildCommand(), CancellationToken.None);

        Assert.Equal("time-limit-but-feasible", outcome.TerminationReason);
        Assert.Equal(1, outcome.ProducedScheduleVersion);
    }

    private static DefaultScheduleOptimizationUseCase Build(
        IScheduleOptimizer optimizer,
        IScheduleRepository schedules,
        IOptimizationRunRepository runs,
        IOptimizationRunMetrics? metrics = null,
        IClock? clock = null)
        => new(
            optimizer,
            schedules,
            runs,
            metrics ?? NoOpOptimizationRunMetrics.Instance,
            clock ?? new FakeClock(),
            NullLogger<DefaultScheduleOptimizationUseCase>.Instance);

    private static ScheduleOptimizationCommand BuildCommand() =>
        new(
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
