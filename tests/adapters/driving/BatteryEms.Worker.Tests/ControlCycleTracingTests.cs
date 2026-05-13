using System.Diagnostics;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.IO;
using BatteryEms.Application.Mpc;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryEms.Worker.Tests;

// RM-M2-06 / LH-MON-003: verifies the hosted service emits a parent
// `bess.control_cycle.execute` activity per asset+tick with a child
// `bess.command_dispatch.write` span around the sink write. Failed
// dispatches must surface as Error-status spans so traces can pivot
// on outcome alongside the existing communication-error metric.
public sealed class ControlCycleTracingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    // Use a per-test unique asset-id so the process-wide
    // ActivityListener isn't polluted by other test classes running in
    // parallel.
    [Fact]
    public async Task Cycle_emits_control_cycle_and_dispatch_spans_with_attributes()
    {
        const string assetId = "tracing-asset-cycle-ok";
        var captured = new List<Activity>();
        using var listener = CaptureBoth(assetId, captured);

        var (service, harness) = Build(assetId);
        await service.StartAsync(CancellationToken.None);
        await harness.WaitForTicksAsync(1);
        await service.StopAsync(CancellationToken.None);

        var cycleSpan = Assert.Single(captured,
            a => a.Source.Name == BessActivitySources.ControlCycleName);
        Assert.Equal("bess.control_cycle.execute", cycleSpan.OperationName);
        Assert.Equal(ActivityStatusCode.Ok, cycleSpan.Status);
        Assert.Equal(assetId, GetTag(cycleSpan, BessActivityTags.AssetId));
        Assert.NotNull(GetTag(cycleSpan, BessActivityTags.CommandMode));
        Assert.NotNull(GetTagObject(cycleSpan, BessActivityTags.PowerKw));

        var dispatchSpan = Assert.Single(captured,
            a => a.Source.Name == BessActivitySources.CommandDispatchName);
        Assert.Equal("bess.command_dispatch.write", dispatchSpan.OperationName);
        Assert.Equal(ActivityStatusCode.Ok, dispatchSpan.Status);
        Assert.Equal(assetId, GetTag(dispatchSpan, BessActivityTags.AssetId));
        Assert.Equal(true, GetTagObject(dispatchSpan, BessActivityTags.DispatchSuccess));

        // Dispatch is a child of the cycle activity (parent-of relationship
        // is what makes failed dispatches actionable in trace UIs).
        Assert.Equal(cycleSpan.SpanId, dispatchSpan.ParentSpanId);
    }

    [Fact]
    public async Task Multi_asset_cycle_emits_one_control_and_dispatch_span_per_asset()
    {
        var captured = new List<Activity>();
        var assetIds = new[] { "tracing-multi-asset-a", "tracing-multi-asset-b" };
        using var listener = CaptureBoth(assetIds, captured);

        var (service, harness) = Build(assetIds);
        await service.StartAsync(CancellationToken.None);
        await harness.WaitForTicksAsync(assetIds.Length);
        await service.StopAsync(CancellationToken.None);

        var cycleAssetIds = captured
            .Where(a => a.Source.Name == BessActivitySources.ControlCycleName)
            .Select(a => GetTag(a, BessActivityTags.AssetId))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var dispatchAssetIds = captured
            .Where(a => a.Source.Name == BessActivitySources.CommandDispatchName)
            .Select(a => GetTag(a, BessActivityTags.AssetId))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(assetIds, cycleAssetIds);
        Assert.Equal(assetIds, dispatchAssetIds);
    }

    [Fact]
    public async Task Failed_dispatch_marks_command_dispatch_span_as_error()
    {
        const string assetId = "tracing-asset-dispatch-fail";
        var captured = new List<Activity>();
        using var listener = CaptureBoth(assetId, captured);

        var (service, harness) = Build(assetId);
        harness.Sink.NextResult = CommandDispatchResult.Failed("simulated", Now);

        await service.StartAsync(CancellationToken.None);
        await harness.WaitForTicksAsync(1);
        await service.StopAsync(CancellationToken.None);

        var dispatchSpan = Assert.Single(captured,
            a => a.Source.Name == BessActivitySources.CommandDispatchName);
        Assert.Equal(ActivityStatusCode.Error, dispatchSpan.Status);
        Assert.Equal("simulated", dispatchSpan.StatusDescription);
        Assert.Equal(false, GetTagObject(dispatchSpan, BessActivityTags.DispatchSuccess));
        Assert.Equal("simulated", GetTag(dispatchSpan, BessActivityTags.DispatchReason));
    }

    [Fact]
    public async Task Cycle_exception_marks_control_cycle_span_as_error()
    {
        const string assetId = "tracing-asset-cycle-throw";
        var captured = new List<Activity>();
        using var listener = CaptureBoth(assetId, captured);

        var (service, harness) = Build(assetId);
        harness.Cycle.Throw = true;

        await service.StartAsync(CancellationToken.None);
        await harness.WaitForTicksAsync(1);
        await service.StopAsync(CancellationToken.None);

        var cycleSpan = Assert.Single(captured,
            a => a.Source.Name == BessActivitySources.ControlCycleName);
        Assert.Equal(ActivityStatusCode.Error, cycleSpan.Status);
        Assert.Contains("simulated control-cycle failure", cycleSpan.StatusDescription, StringComparison.Ordinal);
        // No dispatch span because the cycle threw before reaching the
        // sink write — the trace makes that visible without parsing logs.
        Assert.DoesNotContain(captured, a => a.Source.Name == BessActivitySources.CommandDispatchName);
    }

    private static ActivityListener CaptureBoth(string assetIdFilter, List<Activity> sink)
    {
        return CaptureBoth([assetIdFilter], sink);
    }

    private static ActivityListener CaptureBoth(IReadOnlyCollection<string> assetIdFilter, List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == BessActivitySources.ControlCycleName
                || source.Name == BessActivitySources.CommandDispatchName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                var assetTag = GetTagObject(activity, BessActivityTags.AssetId)?.ToString();
                if (assetTag is not null && assetIdFilter.Contains(assetTag))
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

    private static (ControlCycleHostedService Service, Harness Harness) Build(params string[] assetIds)
    {
        var assets = new InMemoryBatteryAssetRegistry(
            assetIds.Select(id => CreateAsset(id)));
        var cycle = new SpyControlCycle();
        var sink = new SpyCommandSink();
        var repo = new SpyCommandRepository();
        var metrics = new SpyMetrics();
        var harness = new Harness(cycle, sink, repo, metrics);
        var service = new ControlCycleHostedService(
            cycle,
            assets,
            sink,
            repo,
            new InMemoryMpcRunRepository(),
            metrics,
            new InMemorySnapshotStore(TimeSpan.FromSeconds(10)),
            new FakeClock(),
            new BatteryEms.Application.Markets.InMemoryTimebaseHealthSource(),
            Array.Empty<IMpcDispatchOptimizer>(),
            NullLogger<ControlCycleHostedService>.Instance,
            // 1-hour interval: PeriodicTimer fires the first tick
            // immediately, then waits an hour for the next — by that
            // time the test has already called StopAsync, so each
            // source emits exactly one Activity per asset.
            Options.Create(new WorkerOptions { CycleInterval = TimeSpan.FromHours(1) }));
        return (service, harness);
    }

    private static BatteryAsset CreateAsset(string id) => new(
        assetId: id,
        capacityKwh: 100,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 100,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private sealed class Harness
    {
        public SpyControlCycle Cycle { get; }
        public SpyCommandSink Sink { get; }
        public SpyCommandRepository Repository { get; }
        public SpyMetrics Metrics { get; }

        public Harness(SpyControlCycle cycle, SpyCommandSink sink, SpyCommandRepository repo, SpyMetrics metrics)
        {
            Cycle = cycle;
            Sink = sink;
            Repository = repo;
            Metrics = metrics;
        }

        public async Task WaitForTicksAsync(int minimumCalls)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (Cycle.Calls.Count >= minimumCalls)
                {
                    return;
                }
                await Task.Delay(20);
            }
            throw new TimeoutException($"Hosted service did not reach {minimumCalls} cycle calls within 2 s.");
        }
    }

    private sealed class SpyControlCycle : IControlCycleUseCase
    {
        public List<string> Calls { get; } = new();
        public bool Throw { get; set; }

        public Task<BatteryCommand> ExecuteAsync(string assetId, CancellationToken cancellationToken)
        {
            Calls.Add(assetId);
            if (Throw)
            {
                throw new InvalidOperationException("simulated control-cycle failure");
            }
            return Task.FromResult(BatteryCommand.SafeStop(assetId, Now, TimeSpan.FromSeconds(5), "test", CommandSource.Optimization));
        }
    }

    private sealed class SpyCommandSink : IBatteryCommandSink
    {
        public List<BatteryCommand> Writes { get; } = new();
        public CommandDispatchResult? NextResult { get; set; }

        public Task<CommandDispatchResult> WriteAsync(BatteryCommand command, CancellationToken cancellationToken)
        {
            Writes.Add(command);
            var result = NextResult ?? CommandDispatchResult.Ok(Now, "ok");
            return Task.FromResult(result);
        }
    }

    private sealed class SpyCommandRepository : ICommandRepository
    {
        public List<BatteryCommand> Appended { get; } = new();

        public Task AppendAsync(BatteryCommand command, CommandDispatchResult dispatch, CancellationToken cancellationToken)
        {
            Appended.Add(command);
            return Task.CompletedTask;
        }

        public Task<BatteryCommand?> FindByCommandIdAsync(string commandId, CancellationToken cancellationToken)
            => Task.FromResult<BatteryCommand?>(null);

        public Task<BatteryCommand?> FindLatestAsync(string assetId, CancellationToken cancellationToken)
            => Task.FromResult<BatteryCommand?>(null);
    }

    private sealed class SpyMetrics : IControlCycleMetrics
    {
        public List<(string AssetId, string Component)> CommunicationErrors { get; } = new();

        public void RecordCycleDuration(string assetId, TimeSpan duration) { }
        public void IncrementInvalidSnapshot(string assetId, string reason) { }
        public void IncrementCommunicationError(string assetId, string component)
            => CommunicationErrors.Add((assetId, component));
        public void RecordCommandLatency(string assetId, TimeSpan latency) { }
        public void SetActivePowerKw(string assetId, double valueKw) { }
        public void SetSocPercent(string assetId, double valuePercent) { }
        public void RecordSafeStop(string assetId, string reason) { }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }
}
