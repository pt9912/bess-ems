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

public sealed class DefaultIntradayReoptimizationUseCaseTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    // Static readonly arrays — CA1861 forbids inline `new[] {…}`
    // literals in test-method arguments because they reallocate
    // per-call.
    private static readonly double[] FourHourlyPowers = { 10.0, 20.0, 30.0, 40.0 };
    private static readonly double[] TwoHourlyPowers = { 10.0, 20.0 };
    private static readonly double[] TwoIdenticalNinety = { 99.0, 99.0 };
    private static readonly double[] OneNinety = { 99.0 };
    private static readonly double[] TwoIdenticalFifty = { 50.0, 50.0 };
    private static readonly double[] OneFifty = { 50.0 };
    private static readonly double[] FiftyThenSixty = { 50.0, 60.0 };
    private static readonly double[] OneOne = { 1.0 };
    private static readonly int?[] ExpectedSerialisedVersions = { 4, 5 };
    private static readonly int[] ExpectedSerialisedBaseVersions = { 3, 4 };

    [Fact]
    public async Task Happy_path_combines_past_windows_with_new_optimised_windows_and_bumps_version()
    {
        // Existing v3 with four 1-h windows: 12:00, 13:00, 14:00, 15:00.
        // Reopt at 14:00 (boundary between window[1] and window[2]).
        // Past = windows[0..1] (12-13, 13-14), future = windows[2..3] (14-15, 15-16).
        // Optimiser produces v4 with new windows for [14, 16). Combined
        // schedule = past + new, version 4.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: FourHourlyPowers);
        var schedules = new InMemoryScheduleRepository(new[] { existing });
        var residualStart = HorizonStart + TimeSpan.FromHours(2);

        var optimizer = new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true,
            residualWindowPowers: TwoIdenticalNinety));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildCommand(residualStart, residualSteps: 2), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, outcome.Status);
        Assert.Equal(4, outcome.ProducedScheduleVersion);

        var combined = schedules.FindActive("asset-1", ScheduleType.Intraday)!;
        Assert.Equal(4, combined.Version);
        Assert.Equal("DE-LU", combined.MarketBidArea);
        Assert.Equal(4, combined.Windows.Count);
        // Past windows verbatim.
        Assert.Equal(10.0, combined.Windows[0].TargetPowerKw);
        Assert.Equal(20.0, combined.Windows[1].TargetPowerKw);
        // New residual windows from the optimiser.
        Assert.Equal(99.0, combined.Windows[2].TargetPowerKw);
        Assert.Equal(99.0, combined.Windows[3].TargetPowerKw);
    }

    [Fact]
    public async Task Missing_baseline_returns_failed_run_with_intraday_baseline_missing()
    {
        // D-01: no existing Intraday schedule. The use case persists a
        // Failed run with TerminationCode "intraday-baseline-missing"
        // and does NOT call the optimiser.
        var schedules = new InMemoryScheduleRepository();
        var optimizer = new SpyOptimizer(throwOnExecute: true);
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildCommand(HorizonStart, residualSteps: 2), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Null(outcome.ProducedScheduleVersion);
        Assert.StartsWith("intraday-baseline-missing", outcome.TerminationReason, StringComparison.Ordinal);

        var stored = await runs.FindByIdAsync(outcome.RunId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("intraday-baseline-missing", stored!.TerminationCode);
        Assert.Equal("intraday-reopt-precheck", stored.SolverName);
        Assert.Null(optimizer.LastRequest);
    }

    [Fact]
    public async Task Misaligned_residual_start_returns_failed_run_with_residual_start_not_aligned()
    {
        // D-02: residualStart=12:30 falls inside windows[0] [12:00, 13:00).
        // The use case persists a Failed run with TerminationCode
        // "residual-start-not-aligned" and does NOT call the optimiser.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: FourHourlyPowers);
        var schedules = new InMemoryScheduleRepository(new[] { existing });
        var residualStart = HorizonStart + TimeSpan.FromMinutes(30);

        var optimizer = new SpyOptimizer(throwOnExecute: true);
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildCommand(residualStart, residualSteps: 1), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Null(outcome.ProducedScheduleVersion);
        Assert.StartsWith("residual-start-not-aligned", outcome.TerminationReason, StringComparison.Ordinal);

        var stored = await runs.FindByIdAsync(outcome.RunId, CancellationToken.None);
        Assert.Equal("residual-start-not-aligned", stored!.TerminationCode);
        Assert.Equal("intraday-reopt-precheck", stored.SolverName);
        // Inputs include the existing schedule reference (we DID read it).
        Assert.Single(stored.Inputs);
        Assert.Equal(3, stored.Inputs[0].Version);
        Assert.Null(optimizer.LastRequest);

        // Existing schedule untouched.
        Assert.Equal(3, schedules.FindActive("asset-1", ScheduleType.Intraday)!.Version);
    }

    [Fact]
    public async Task Solver_failure_persists_failed_run_without_replace()
    {
        // The optimiser returns a Failed run with no ProducedSchedule.
        // Replace must NOT happen. The existing schedule stays at v3.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: TwoHourlyPowers);
        var schedules = new InMemoryScheduleRepository(new[] { existing });

        var optimizer = new SpyOptimizer(req => BuildResidualResult(req,
            includeSchedule: false, status: OptimizationSolverStatus.Infeasible,
            terminationCode: "solver-infeasible"));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildCommand(HorizonStart + TimeSpan.FromHours(1), residualSteps: 1), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Infeasible, outcome.Status);
        Assert.Null(outcome.ProducedScheduleVersion);
        Assert.Equal("solver-infeasible", outcome.TerminationReason);

        // Existing v3 still active, untouched.
        var unchanged = schedules.FindActive("asset-1", ScheduleType.Intraday)!;
        Assert.Equal(3, unchanged.Version);
        Assert.Equal(2, unchanged.Windows.Count);
    }

    [Fact]
    public async Task Cas_conflict_on_replace_persists_failed_run_with_concurrent_version_conflict()
    {
        // Sibling-replica race: the optimiser produces a v4 candidate but
        // a sibling has advanced the store to v4 before our Replace
        // lands. The use case must surface a Failed run with
        // TerminationCode "concurrent-version-conflict" and leave the
        // existing schedule intact.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: TwoHourlyPowers);
        var schedules = new ConflictingScheduleRepository(seed: existing, actualVersionAtReplace: 4);

        var optimizer = new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true,
            residualWindowPowers: OneNinety));
        var runs = new InMemoryOptimizationRunRepository();
        var clock = new FakeClock { UtcNow = HorizonStart + TimeSpan.FromHours(5) };
        var useCase = Build(optimizer, schedules, runs, clock: clock);

        var outcome = await useCase.ExecuteAsync(BuildCommand(HorizonStart + TimeSpan.FromHours(1), residualSteps: 1), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Null(outcome.ProducedScheduleVersion);
        Assert.Equal("concurrent-version-conflict:expected=3,actual=4", outcome.TerminationReason);

        var stored = await runs.FindByIdAsync(outcome.RunId, CancellationToken.None);
        Assert.Equal("schedule-cas-guard", stored!.SolverName);
        Assert.Equal("concurrent-version-conflict", stored.TerminationCode);
        Assert.Equal("expected=3,actual=4", stored.TerminationDetail);
        Assert.Equal(clock.UtcNow, stored.CreatedAt);
    }

    [Fact]
    public async Task Reserve_bands_for_residual_horizon_are_passed_to_the_optimiser()
    {
        // RM-M4-02: reserves overlapping the residual horizon flow into
        // the optimiser request. Reserves outside the residual range
        // are filtered by IReserveRepository.FindActive.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: FourHourlyPowers);
        var schedules = new InMemoryScheduleRepository(new[] { existing });
        var residualStart = HorizonStart + TimeSpan.FromHours(2);
        var residualBand = new ReserveBand(
            "asset-1", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            residualStart, residualStart + TimeSpan.FromHours(2), 5);
        // A band that does NOT overlap the residual range — must be filtered out.
        var pastBand = new ReserveBand(
            "asset-1", ReserveProduct.Afrr, ReserveDirection.Up,
            HorizonStart, HorizonStart + TimeSpan.FromHours(1), 7);
        var reserves = new InMemoryReserveRepository(new[] { residualBand, pastBand });

        var optimizer = new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true,
            residualWindowPowers: TwoIdenticalFifty));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs, reserves: reserves);

        await useCase.ExecuteAsync(BuildCommand(residualStart, residualSteps: 2), CancellationToken.None);

        var passedReserves = optimizer.LastRequest!.Reserves;
        Assert.Single(passedReserves);
        Assert.Equal(ReserveProduct.Fcr, passedReserves[0].Product);
        Assert.Equal(5.0, passedReserves[0].PowerKw);
    }

    [Fact]
    public async Task Use_case_calls_replace_with_existing_version_not_produced_version()
    {
        // Wiring pin (analog to the day-ahead Use_case_replaces_with_
        // existing_version_not_produced_version): the use case must
        // pass `existing.Version` as expectedBaseVersion, not
        // `produced.Version`. The latter would be a no-op CAS.
        var existing = BuildExistingSchedule(version: 5, hourlyPowers: TwoHourlyPowers);
        var schedules = new RecordingScheduleRepository(seed: existing);

        var optimizer = new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true,
            residualWindowPowers: OneNinety));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        await useCase.ExecuteAsync(BuildCommand(HorizonStart + TimeSpan.FromHours(1), residualSteps: 1), CancellationToken.None);

        var call = Assert.Single(schedules.ReplaceCalls);
        Assert.Equal(5, call.ExpectedBaseVersion);
        Assert.Equal(6, call.SchedulePassedIn.Version);
    }

    [Fact]
    public async Task Window_boundary_at_existing_horizon_end_is_aligned()
    {
        // residualStart equal to existing.HorizonEnd is a valid boundary
        // (D-02 half-open semantics). All windows are past; the
        // optimiser produces fresh future-only windows. This pins the
        // edge-of-existing-schedule case.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: TwoHourlyPowers);
        var schedules = new InMemoryScheduleRepository(new[] { existing });
        var residualStart = existing.HorizonEnd; // 14:00, the End of the last window

        var optimizer = new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true,
            residualWindowPowers: OneFifty));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildCommand(residualStart, residualSteps: 1), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, outcome.Status);
        Assert.Equal(4, outcome.ProducedScheduleVersion);
        var combined = schedules.FindActive("asset-1", ScheduleType.Intraday)!;
        Assert.Equal(3, combined.Windows.Count); // 2 past + 1 new
    }

    [Fact]
    public async Task Window_boundary_at_existing_horizon_start_is_aligned()
    {
        // residualStart equal to existing.HorizonStart is also a valid
        // boundary. No past windows; the entire schedule gets reoptimised.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: TwoHourlyPowers);
        var schedules = new InMemoryScheduleRepository(new[] { existing });

        var optimizer = new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true,
            residualWindowPowers: FiftyThenSixty));
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var outcome = await useCase.ExecuteAsync(BuildCommand(HorizonStart, residualSteps: 2), CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, outcome.Status);
        var combined = schedules.FindActive("asset-1", ScheduleType.Intraday)!;
        Assert.Equal(2, combined.Windows.Count); // 0 past + 2 new
        Assert.Equal(50.0, combined.Windows[0].TargetPowerKw);
        Assert.Equal(60.0, combined.Windows[1].TargetPowerKw);
    }

    [Fact]
    public async Task Concurrent_calls_for_same_asset_are_serialised_per_key()
    {
        // Without per-(asset, Intraday) lock both calls would read v3,
        // both produce v4, both Replace. With the lock the second call
        // sees v4 and produces v5.
        var existing = BuildExistingSchedule(version: 3, hourlyPowers: TwoHourlyPowers);
        var schedules = new InMemoryScheduleRepository(new[] { existing });
        var observedBaseVersions = new System.Collections.Concurrent.ConcurrentBag<int>();

        var optimizer = new SpyOptimizer(req =>
        {
            observedBaseVersions.Add(req.BaseScheduleVersion);
            Thread.Sleep(20);
            return BuildResidualResult(req, includeSchedule: true,
                residualWindowPowers: OneFifty);
        });
        var runs = new InMemoryOptimizationRunRepository();
        var useCase = Build(optimizer, schedules, runs);

        var first = useCase.ExecuteAsync(BuildCommand(HorizonStart + TimeSpan.FromHours(1), residualSteps: 1), CancellationToken.None);
        var second = useCase.ExecuteAsync(BuildCommand(HorizonStart + TimeSpan.FromHours(1), residualSteps: 1), CancellationToken.None);
        await Task.WhenAll(first, second);

        var versions = new[] { (await first).ProducedScheduleVersion, (await second).ProducedScheduleVersion }
            .OrderBy(v => v).ToArray();
        Assert.Equal(ExpectedSerialisedVersions, versions);
        var orderedBases = observedBaseVersions.OrderBy(v => v).ToArray();
        Assert.Equal(ExpectedSerialisedBaseVersions, orderedBases);
    }

    [Fact]
    public async Task Null_command_throws()
    {
        var useCase = Build(
            new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true, residualWindowPowers: OneOne)),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Dispose_makes_subsequent_execute_throw_object_disposed()
    {
        var useCase = Build(
            new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true, residualWindowPowers: OneOne)),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository());
        useCase.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            useCase.ExecuteAsync(BuildCommand(HorizonStart, residualSteps: 1), CancellationToken.None));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var useCase = Build(
            new SpyOptimizer(req => BuildResidualResult(req, includeSchedule: true, residualWindowPowers: OneOne)),
            new InMemoryScheduleRepository(),
            new InMemoryOptimizationRunRepository());
        useCase.Dispose();
        useCase.Dispose();
    }

    private static DefaultIntradayReoptimizationUseCase Build(
        IScheduleOptimizer optimizer,
        IScheduleRepository schedules,
        IOptimizationRunRepository runs,
        IOptimizationRunMetrics? metrics = null,
        IClock? clock = null,
        IReserveRepository? reserves = null)
        => new(
            optimizer,
            schedules,
            reserves ?? new InMemoryReserveRepository(),
            runs,
            metrics ?? NoOpOptimizationRunMetrics.Instance,
            clock ?? new FakeClock(),
            NullLogger<DefaultIntradayReoptimizationUseCase>.Instance);

    private static IntradayReoptimizationCommand BuildCommand(
        DateTimeOffset residualStart,
        int residualSteps) =>
        new(
            assetId: "asset-1",
            asset: TestFixtures.CreateAsset(),
            residualStart: residualStart,
            horizonEnd: residualStart + TimeSpan.FromHours(residualSteps),
            timeStep: TimeSpan.FromHours(1));

    private static Schedule BuildExistingSchedule(int version, double[] hourlyPowers)
    {
        var windows = new List<ScheduleWindow>();
        for (var i = 0; i < hourlyPowers.Length; i++)
        {
            var start = HorizonStart + TimeSpan.FromHours(i);
            windows.Add(new ScheduleWindow(start, start + TimeSpan.FromHours(1), hourlyPowers[i]));
        }
        return new Schedule(
            assetId: "asset-1",
            type: ScheduleType.Intraday,
            marketBidArea: "DE-LU",
            version: version,
            windows: windows);
    }

    private static ScheduleOptimizationResult BuildResidualResult(
        ScheduleOptimizationRequest request,
        bool includeSchedule,
        double[]? residualWindowPowers = null,
        OptimizationSolverStatus status = OptimizationSolverStatus.Optimal,
        string terminationCode = "ok")
    {
        Schedule? schedule = null;
        ScheduleReference? produced = null;
        if (includeSchedule)
        {
            ArgumentNullException.ThrowIfNull(residualWindowPowers);
            var producedVersion = request.BaseScheduleVersion + 1;
            var windows = new List<ScheduleWindow>();
            for (var i = 0; i < residualWindowPowers.Length; i++)
            {
                var start = request.HorizonStart + TimeSpan.FromTicks(request.TimeStep.Ticks * i);
                windows.Add(new ScheduleWindow(start, start + request.TimeStep, residualWindowPowers[i]));
            }
            schedule = new Schedule(
                assetId: request.AssetId,
                type: ScheduleType.Intraday,
                marketBidArea: request.MarketBidArea,
                version: producedVersion,
                windows: windows);
            produced = new ScheduleReference(schedule.AssetId, schedule.Type, schedule.Version);
        }

        var run = new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: "spy-solver",
            status: status,
            horizonStart: request.HorizonStart,
            horizonEnd: request.HorizonEnd,
            timeStep: request.TimeStep,
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: TimeSpan.FromMilliseconds(1),
            terminationCode: terminationCode,
            terminationDetail: null,
            createdAt: HorizonStart,
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: produced);
        return new ScheduleOptimizationResult(run, schedule);
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

    private sealed class ConflictingScheduleRepository : IScheduleRepository
    {
        private readonly Schedule _seed;
        private readonly int _actualVersionAtReplace;

        public ConflictingScheduleRepository(Schedule seed, int actualVersionAtReplace)
        {
            _seed = seed;
            _actualVersionAtReplace = actualVersionAtReplace;
        }

        public IEnumerable<Schedule> FindAll(string assetId) => new[] { _seed };

        public Schedule? FindActive(string assetId, ScheduleType type) => _seed;

        public void Replace(Schedule schedule, int expectedBaseVersion) =>
            throw new ScheduleConcurrencyConflictException(
                schedule.AssetId, schedule.Type, expectedBaseVersion, _actualVersionAtReplace);
    }

    private sealed class RecordingScheduleRepository : IScheduleRepository
    {
        private readonly Schedule _seed;
        public List<(int ExpectedBaseVersion, Schedule SchedulePassedIn)> ReplaceCalls { get; } = new();

        public RecordingScheduleRepository(Schedule seed)
        {
            _seed = seed;
        }

        public IEnumerable<Schedule> FindAll(string assetId) => new[] { _seed };

        public Schedule? FindActive(string assetId, ScheduleType type) => _seed;

        public void Replace(Schedule schedule, int expectedBaseVersion)
        {
            ReplaceCalls.Add((expectedBaseVersion, schedule));
        }
    }
}
