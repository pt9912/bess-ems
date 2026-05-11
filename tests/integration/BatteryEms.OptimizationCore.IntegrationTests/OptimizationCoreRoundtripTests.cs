using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-B §4 Sub-Slice-B: 5 pinned Roundtrip-Pins gegen den
// In-Process TestSidecar. Per-class-Lifetime; jeder Test baut sich
// seine eigene Sidecar-Instanz auf einem Per-Test-UDS.
[Trait("Category", "Integration")]
[CollectionDefinition("OptimizationCore Integration", DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711",
    Justification = "xUnit CollectionDefinition convention requires the 'Collection' suffix.")]
public sealed class OptimizationCoreIntegrationCollection { }

[Trait("Category", "Integration")]
[Collection("OptimizationCore Integration")]
public sealed class OptimizationCoreRoundtripTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(4);
    private static readonly TimeSpan TimeStep = TimeSpan.FromHours(1);

    // Pin 1: Health-Probe success. Adapter ConnectAsync + interner
    // Health-Call gegen den OptimalAlwaysSucceedsStub liefert
    // SERVING → kein Throw.
    [Fact]
    public async Task Health_probe_succeeds_against_test_sidecar()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<OptimalAlwaysSucceedsStub>();
        await using var client = new OptimizationCoreClient(
            Defaults.ForHilSimulator(sidecar.Endpoint));

        await client.ConnectAsync(default);
        var health = await client.Client.HealthAsync(new HealthRequest());

        Assert.Equal(HealthResponse.Types.Status.Serving, health.Status);
    }

    // Pin 2: Version-Probe + Feature-Match. Adapter prüft pre-Optimize
    // gegen `ExpectedContractVersion=1.0.0` und `has-usable-solution`-
    // Feature.
    [Fact]
    public async Task Version_probe_compatibility_check_passes()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<OptimalAlwaysSucceedsStub>();
        await using var client = new OptimizationCoreClient(
            Defaults.ForHilSimulator(sidecar.Endpoint));

        await client.ConnectAsync(default);
        var version = await client.Client.VersionAsync(new VersionRequest());

        Assert.Equal(OptimalAlwaysSucceedsStub.ContractVersion, version.ContractVersion);
        Assert.Contains("has-usable-solution", version.Features);
    }

    // Pin 3: Optimize-Success-Optimal. Adapter führt den vollen Flow
    // (Connect → Health → Version → Optimize-streaming) und liefert
    // einen `ScheduleOptimizationResult` mit `Status=Optimal` +
    // ProducedSchedule.
    [Fact]
    public async Task Optimize_success_produces_optimal_run_with_schedule()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<OptimalAlwaysSucceedsStub>();
        var optimizer = BuildOptimizer(sidecar);

        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);
        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal(request.AssetId, result.ProducedSchedule!.AssetId);
        Assert.Equal((int)(Horizon.Ticks / TimeStep.Ticks),
            result.ProducedSchedule.Windows.Count);
    }

    // Pin 4: Streaming-Progress wird vom Adapter konsumiert ohne den
    // finalen Result-Pfad zu blockieren. Wir scripten 3 Progress-
    // Updates plus ein finales Result und prüfen, dass der Adapter
    // den finalen Pfad trifft.
    [Fact]
    public async Task Optimize_streaming_progress_does_not_block_final_result()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            async (req, stream, ctx) =>
            {
                // Drei Progress-Updates ohne Result.
                for (var i = 0; i < 3; i++)
                {
                    await stream.WriteAsync(new OptimizeUpdate
                    {
                        Progress = new OptimizeProgress
                        {
                            StepIndex = i,
                            ObjectiveSoFar = i * 10.0,
                        },
                    });
                }
                // Final Result mit Single-Window-Schedule.
                var horizonStart = req.HorizonStart.ToDateTimeOffset();
                var horizonEnd = req.HorizonEnd.ToDateTimeOffset();
                var result = new OptimizeResult
                {
                    SolverStatus = OptimizeResult.Types.SolverStatus.Optimal,
                    HasUsableSolution = true,
                    SolutionQuality = "optimal",
                    SolverName = "scripted-stream-stub",
                    TerminationCode = "OPTIMAL",
                };
                result.SchedulePoints.Add(new SchedulePoint
                {
                    WindowStart = Timestamp.FromDateTimeOffset(horizonStart),
                    WindowEnd = Timestamp.FromDateTimeOffset(horizonEnd),
                    TargetPowerKw = 5.0,
                });
                await stream.WriteAsync(new OptimizeUpdate { Result = result });
            });

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal(5.0, result.ProducedSchedule!.Windows[0].TargetPowerKw);
    }

    // Pin 5: Cancellation mid-stream. Cancel-Token wird gefeuert
    // während der Sidecar noch Progress-Updates streamt → Adapter
    // gibt einen Failed-Run mit `TransportCancelled`-Outcome zurück.
    [Fact]
    public async Task Optimize_cancellation_mid_stream_returns_failed_run()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        using var cts = new CancellationTokenSource();

        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            async (req, stream, ctx) =>
            {
                // Erst-Progress emittieren, dann den Cancel-Trigger
                // pullen, dann auf Cancel warten — der Adapter wirft
                // OperationCanceled in `ReadAllAsync` und der Wrap-
                // Pfad mappt das zu Failed/Cancelled.
                await stream.WriteAsync(new OptimizeUpdate
                {
                    Progress = new OptimizeProgress { StepIndex = 0, ObjectiveSoFar = 0.0 },
                });
                cts.Cancel();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ctx.CancellationToken);
                }
                catch (OperationCanceledException) { /* erwartet */ }
            });

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, cts.Token);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("Cancelled", result.Run.TerminationCode);
    }

    private static OptimizationCoreScheduleOptimizer BuildOptimizer(
        EmbeddedOptimizationCoreSidecar sidecar)
    {
        var options = Defaults.ForHilSimulator(sidecar.Endpoint);
        var client = new OptimizationCoreClient(options);
        return new OptimizationCoreScheduleOptimizer(
            client,
            options,
            new Defaults.FixedClock(),
            NullLogger<OptimizationCoreScheduleOptimizer>.Instance);
    }
}
