using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.IO;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryEms.Worker.Tests;

public sealed class ControlCycleHostedServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Tick_runs_cycle_for_every_registered_asset_and_persists_command()
    {
        var (service, harness) = Build("asset-1", "asset-2");
        await service.StartAsync(CancellationToken.None);
        await harness.WaitForTicksAsync(2);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains("asset-1", harness.Cycle.Calls);
        Assert.Contains("asset-2", harness.Cycle.Calls);
        Assert.NotEmpty(harness.Sink.Writes);
        Assert.NotEmpty(harness.Repository.Appended);
    }

    [Fact]
    public async Task Failed_dispatch_increments_communication_error_metric_and_keeps_loop_running()
    {
        var (service, harness) = Build("asset-1");
        harness.Sink.NextResult = CommandDispatchResult.Failed("simulated", Now);

        await service.StartAsync(CancellationToken.None);
        await harness.WaitForTicksAsync(1);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(harness.Metrics.CommunicationErrors,
            e => e.AssetId == "asset-1" && e.Component == "command-sink");
    }

    [Fact]
    public async Task Cycle_exception_increments_metric_and_does_not_stop_the_loop()
    {
        var (service, harness) = Build("asset-1");
        harness.Cycle.Throw = true;

        await service.StartAsync(CancellationToken.None);
        await harness.WaitForTicksAsync(1);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(harness.Metrics.CommunicationErrors,
            e => e.AssetId == "asset-1" && e.Component == "control-cycle");
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
            metrics,
            new FakeClock(),
            new BatteryEms.Application.Markets.InMemoryTimebaseHealthSource(),
            NullLogger<ControlCycleHostedService>.Instance,
            Options.Create(new WorkerOptions { CycleInterval = TimeSpan.FromMilliseconds(20) }));
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
