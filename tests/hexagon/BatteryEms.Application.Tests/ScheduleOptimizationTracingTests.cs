using System.Diagnostics;
using System.Globalization;
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

// RM-M2-06 / LH-MON-003: verifies the schedule-optimisation use case
// emits the expected Activity with the LH-MON-001-aligned tag set.
// Uses ActivityListener (BCL) so the test assembly does not need a
// reference to the OpenTelemetry SDK — the production wiring registers
// the SDK as a separate listener; both can co-exist.
public sealed class ScheduleOptimizationTracingTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    // Use a per-test unique asset-id so the process-wide ActivityListener
    // does not pick up Activities from other tests running in parallel
    // (xUnit runs test classes concurrently). The listener filters
    // captured activities by AssetId tag.
    [Fact]
    public async Task Optimisation_run_emits_activity_with_attributes()
    {
        const string assetId = "tracing-asset-optimal";
        var captured = new List<Activity>();
        using var listener = CaptureSource(BessActivitySources.ScheduleOptimizationName, assetId, captured);

        var optimizer = new SpyOptimizer(req => BuildResult(
            req, OptimizationSolverStatus.Optimal, includeSchedule: true, assetId: assetId));
        var useCase = new DefaultScheduleOptimizationUseCase(
            optimizer,
            new InMemoryScheduleRepository(),
            new InMemoryReserveRepository(),
            new InMemoryOptimizationRunRepository(),
            NoOpOptimizationRunMetrics.Instance,
            new FakeClock(),
            NullLogger<DefaultScheduleOptimizationUseCase>.Instance);

        var outcome = await useCase.ExecuteAsync(BuildCommand(assetId), CancellationToken.None);

        var activity = Assert.Single(captured);
        Assert.Equal("bess.schedule_optimization.run", activity.OperationName);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal(assetId, GetTag(activity, BessActivityTags.AssetId));
        Assert.Equal(outcome.RunId.ToString(), GetTag(activity, BessActivityTags.RunId));
        Assert.Equal("Optimal", GetTag(activity, BessActivityTags.SolverStatus));
        Assert.Equal("ok", GetTag(activity, BessActivityTags.TerminationReason));
        Assert.Equal(1, Convert.ToInt32(GetTagObject(activity, BessActivityTags.ProducedScheduleVersion), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Failed_solver_marks_activity_with_error_status()
    {
        const string assetId = "tracing-asset-failed";
        var captured = new List<Activity>();
        using var listener = CaptureSource(BessActivitySources.ScheduleOptimizationName, assetId, captured);

        var optimizer = new SpyOptimizer(_ => throw new InvalidOperationException("solver-crash"));
        var useCase = new DefaultScheduleOptimizationUseCase(
            optimizer,
            new InMemoryScheduleRepository(),
            new InMemoryReserveRepository(),
            new InMemoryOptimizationRunRepository(),
            NoOpOptimizationRunMetrics.Instance,
            new FakeClock(),
            NullLogger<DefaultScheduleOptimizationUseCase>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(BuildCommand(assetId), CancellationToken.None));

        var activity = Assert.Single(captured);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("solver-crash", activity.StatusDescription);
    }

    private static ActivityListener CaptureSource(string sourceName, string assetIdFilter, List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                // Filter by AssetId so concurrent tests on the same source
                // can't poison each other's captures.
                var assetTag = GetTagObject(activity, BessActivityTags.AssetId)?.ToString();
                if (assetTag == assetIdFilter)
                {
                    sink.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string? GetTag(Activity activity, string key)
    {
        var obj = GetTagObject(activity, key);
        return obj?.ToString();
    }

    private static object? GetTagObject(Activity activity, string key)
    {
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key == key)
            {
                return tag.Value;
            }
        }
        return null;
    }

    private static ScheduleOptimizationCommand BuildCommand(string assetId) => new(
        assetId: assetId,
        scheduleType: ScheduleType.DayAhead,
        asset: TestFixtures.CreateAsset(assetId),
        horizonStart: HorizonStart,
        horizonEnd: HorizonStart + TimeSpan.FromHours(1),
        timeStep: TimeSpan.FromHours(1));

    private static ScheduleOptimizationResult BuildResult(
        ScheduleOptimizationRequest request,
        OptimizationSolverStatus status,
        bool includeSchedule,
        string assetId,
        string terminationCode = "ok")
    {
        Schedule? schedule = null;
        ScheduleReference? produced = null;
        if (includeSchedule)
        {
            var version = request.BaseScheduleVersion + 1;
            schedule = new Schedule(
                assetId, ScheduleType.DayAhead, request.MarketBidArea, version,
                new List<ScheduleWindow>
                {
                    new(HorizonStart, HorizonStart + TimeSpan.FromHours(1), 0),
                });
            produced = new ScheduleReference(schedule.AssetId, schedule.Type, schedule.Version);
        }
        var run = new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: assetId,
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
            terminationDetail: null,
            createdAt: HorizonStart,
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: produced);
        return new ScheduleOptimizationResult(run, schedule);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via test setup.")]
    private sealed class SpyOptimizer : IScheduleOptimizer
    {
        private readonly Func<ScheduleOptimizationRequest, ScheduleOptimizationResult> _factory;

        public SpyOptimizer(Func<ScheduleOptimizationRequest, ScheduleOptimizationResult> factory)
        {
            _factory = factory;
        }

        public Task<ScheduleOptimizationResult> OptimizeAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_factory(request));
        }
    }
}
