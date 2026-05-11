using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-B §4 Sub-Slice-B: 4 pinned Negative-Pins. Decken
// die Fallback-Matrix-Pfade aus plan-RM-M5 §Fallback-Matrix.
[Trait("Category", "Integration")]
[Collection("OptimizationCore Integration")]
public sealed class OptimizationCoreNegativeTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(4);
    private static readonly TimeSpan TimeStep = TimeSpan.FromHours(1);

    // Pin 1: Deadline-Exceeded → `solver_time_limit`-Fallback. Sidecar
    // simuliert einen hängenden Solver (Stream bleibt offen über die
    // Adapter-Deadline hinaus).
    [Fact]
    public async Task Deadline_exceeded_returns_failed_run_with_time_limit_status()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            async (req, stream, ctx) =>
            {
                // Hängen lassen bis der Adapter-Deadline-Timer (siehe
                // unten, RequestDeadline=300ms) zuschnappt.
                await Task.Delay(Timeout.InfiniteTimeSpan, ctx.CancellationToken);
            });

        var optimizer = BuildOptimizer(sidecar, requestDeadline: TimeSpan.FromMilliseconds(300));
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.TimeLimit, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("DeadlineExceeded", result.Run.TerminationCode);
    }

    // Pin 2: Server-side-Crash mid-Request → `sidecar_unavailable`-
    // Fallback. Sidecar wirft RpcException(Unavailable) sofort beim
    // Optimize.
    [Fact]
    public async Task Sidecar_unavailable_returns_failed_run_with_failed_status()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            (req, stream, ctx) =>
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    "simulated-sidecar-crash")));

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("Unavailable", result.Run.TerminationCode);
    }

    // Pin 3: Infeasible-Sidecar-Result → keine neue Schedule-Version
    // (PersistSchedule=false aus StatusMapper).
    [Fact]
    public async Task Infeasible_sidecar_result_produces_no_schedule()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            async (req, stream, ctx) =>
            {
                await stream.WriteAsync(new OptimizeUpdate
                {
                    Result = new OptimizeResult
                    {
                        SolverStatus = OptimizeResult.Types.SolverStatus.Infeasible,
                        HasUsableSolution = false,
                        SolutionQuality = "none",
                        TerminationCode = "INFEASIBLE_PROVED",
                        SolverName = "scripted-infeasible-stub",
                    },
                });
            });

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Infeasible, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("INFEASIBLE_PROVED", result.Run.TerminationCode);
    }

    // Pin 4: Invalid-Trajectory-Output → verwirft-Result. Sidecar
    // sagt Optimal + has_usable_solution=true ABER liefert eine
    // ungültige Trajektorie (NaN-Power oder überlappende Windows).
    // Adapter erkennt das vor der Schedule-Konstruktion und failed
    // mit `invalid-trajectory`.
    [Fact]
    public async Task Invalid_trajectory_output_is_rejected_as_failed_run()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            async (req, stream, ctx) =>
            {
                var result = new OptimizeResult
                {
                    SolverStatus = OptimizeResult.Types.SolverStatus.Optimal,
                    HasUsableSolution = true,
                    SolutionQuality = "optimal",
                    TerminationCode = "OPTIMAL",
                    SolverName = "scripted-bad-trajectory-stub",
                };
                // NaN-Power → fail-closed via Adapter.
                var horizonStart = req.HorizonStart.ToDateTimeOffset();
                var horizonEnd = req.HorizonEnd.ToDateTimeOffset();
                result.SchedulePoints.Add(new SchedulePoint
                {
                    WindowStart = Timestamp.FromDateTimeOffset(horizonStart),
                    WindowEnd = Timestamp.FromDateTimeOffset(horizonEnd),
                    TargetPowerKw = double.NaN,
                });
                await stream.WriteAsync(new OptimizeUpdate { Result = result });
            });

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("invalid-trajectory", result.Run.TerminationCode);
        Assert.NotNull(result.Run.TerminationDetail);
        Assert.Contains("non-finite-power", result.Run.TerminationDetail, StringComparison.Ordinal);
    }

    private static OptimizationCoreScheduleOptimizer BuildOptimizer(
        EmbeddedOptimizationCoreSidecar sidecar,
        TimeSpan? requestDeadline = null)
    {
        var options = Defaults.ForHilSimulator(sidecar.Endpoint) with
        {
            RequestDeadline = requestDeadline ?? TimeSpan.FromSeconds(5),
        };
        var client = new OptimizationCoreClient(options);
        return new OptimizationCoreScheduleOptimizer(
            client,
            options,
            new Defaults.FixedClock(),
            NullLogger<OptimizationCoreScheduleOptimizer>.Instance);
    }
}
