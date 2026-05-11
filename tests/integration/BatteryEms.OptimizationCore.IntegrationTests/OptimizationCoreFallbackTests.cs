using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-C Korrektur-Pass §5.1: lokaler Fallback-Optimizer +
// Plan-Validator-Integration in den OptimizeAsync-Flow. Decken plan-
// RM-M5 §Fallback-Matrix Zeile „Timeout/Deadline oder Unavailable
// vor Ergebnis" und §Fallback-Plan-Gueltigkeit Kontext-Stempel-
// /Horizon-Achsen (Telemetrie-Drift bleibt skip'd, da der Adapter
// keinen Snapshot im Scope hat).
[Trait("Category", "Integration")]
[Collection("OptimizationCore Integration")]
public sealed class OptimizationCoreFallbackTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(4);
    private static readonly TimeSpan TimeStep = TimeSpan.FromHours(1);

    // Pin 1: Sidecar-Unavailable + Fallback-Optimizer registriert →
    // FallbackCommitted-Terminal mit dem vom Fallback gelieferten
    // Schedule. Kein Sidecar-Optimize-Call hat stattgefunden, weil
    // die Connect-/Health-Phase schon scheitert; Fallback liefert
    // statt no_valid_plan einen frischen Plan.
    [Fact]
    public async Task Transport_failure_with_fallback_returns_fallback_committed_schedule()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            (req, stream, ctx) =>
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    "simulated-sidecar-crash-falls-to-local-fallback")));
        var fallbackStub = new SchedulingStubFallbackOptimizer();

        var optimizer = BuildOptimizer(sidecar, fallback: fallbackStub);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal("scripted-fallback-stub", result.Run.SolverName);
        Assert.Equal(1, fallbackStub.CallCount);
    }

    // Pin 2: Sidecar-Unavailable + KEIN Fallback registriert →
    // FailedNoActivation mit dem Status-Mapper-Outcome (kein
    // no_valid_plan-Code, sondern transport-spezifisch — heute
    // `Unavailable` aus dem StatusMapper). Pin sichert ab dass das
    // alte Verhalten unverändert bleibt wenn der Operator keinen
    // Fallback konfiguriert hat.
    [Fact]
    public async Task Transport_failure_without_fallback_returns_failed_no_activation()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            (req, stream, ctx) =>
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    "no-fallback-configured")));

        var optimizer = BuildOptimizer(sidecar, fallback: null);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("Unavailable", result.Run.TerminationCode);
    }

    // Pin 3: Sidecar-Unavailable + Fallback registriert ABER Fallback
    // wirft selber → no-valid-plan (FailedNoActivation mit dem
    // ursprünglichen transport-Outcome aus dem StatusMapper, weil
    // der Fallback-Versuch keinen brauchbaren Plan geliefert hat).
    [Fact]
    public async Task Transport_failure_with_fallback_that_throws_falls_through_to_failed()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            (req, stream, ctx) =>
                throw new RpcException(new Status(
                    StatusCode.Unavailable, "sidecar-fail")));
        var fallbackStub = new ThrowingFallbackOptimizer();

        var optimizer = BuildOptimizer(sidecar, fallback: fallbackStub);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("Unavailable", result.Run.TerminationCode);
        Assert.Equal(1, fallbackStub.CallCount);
    }

    // Pin 4: Sidecar-Optimize-Success + Fallback registriert → Fallback
    // bleibt unangefasst. Pin sichert ab dass der Happy-Path durch den
    // Fallback-Wrapper nicht reklassifiziert wird (no fallback-call on
    // success).
    [Fact]
    public async Task Sidecar_success_does_not_invoke_fallback()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            (req, stream, ctx) => stream.WriteAsync(BuildOptimalUpdate(req)));
        var fallbackStub = new SchedulingStubFallbackOptimizer();

        var optimizer = BuildOptimizer(sidecar, fallback: fallbackStub);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal("scripted-sidecar-optimal", result.Run.SolverName);
        Assert.Equal(0, fallbackStub.CallCount);
    }

    // Pin 5: Sidecar-Unavailable + Fallback liefert einen Plan mit
    // FREMDEM market_bid_area (Kontext-Stempel-Mismatch) →
    // Plan-Validator rejected den Plan → no-valid-plan. Pin sichert
    // ab dass der Validator-Aufruf in der Laufzeit aktiv ist und
    // nicht nur als DI-Service vorhanden.
    [Fact]
    public async Task Fallback_with_context_mismatch_is_rejected_by_validator()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>().EnqueueOptimize(
            (req, stream, ctx) =>
                throw new RpcException(new Status(
                    StatusCode.Unavailable, "sidecar-fail")));
        // Fallback erzeugt einen Schedule mit asset_id="other-asset"
        // — der Validator schlägt mit FallbackContextMismatch zu.
        var fallbackStub = new ContextMismatchFallbackOptimizer();

        var optimizer = BuildOptimizer(sidecar, fallback: fallbackStub);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        // Fallback wurde aufgerufen (1x) aber durch den Validator
        // rejected; Endresultat ist FailedNoActivation.
        Assert.Equal(1, fallbackStub.CallCount);
        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("Unavailable", result.Run.TerminationCode);
    }

    private static OptimizationCoreScheduleOptimizer BuildOptimizer(
        EmbeddedOptimizationCoreSidecar sidecar,
        IFallbackScheduleOptimizer? fallback)
    {
        var options = Defaults.ForHilSimulator(sidecar.Endpoint);
        var client = new OptimizationCoreClient(options);
        var validator = new DefaultFallbackPlanValidator(
            new FallbackPlanValidatorOptions
            {
                ControlCycleInterval = TimeSpan.FromMinutes(30),
            });
        return new OptimizationCoreScheduleOptimizer(
            client,
            options,
            new InMemoryOptimizationIdempotencyStore(),
            new Defaults.FixedClock(),
            NullLogger<OptimizationCoreScheduleOptimizer>.Instance,
            fallback,
            validator);
    }

    private static OptimizeUpdate BuildOptimalUpdate(OptimizeRequest req)
    {
        var update = new OptimizeUpdate
        {
            Result = new OptimizeResult
            {
                SolverStatus = OptimizeResult.Types.SolverStatus.Optimal,
                HasUsableSolution = true,
                SolutionQuality = "optimal",
                TerminationCode = "OPTIMAL",
                SolverName = "scripted-sidecar-optimal",
                ObjectiveValue = 0,
            },
        };
        var horizonStart = req.HorizonStart.ToDateTimeOffset();
        var horizonEnd = req.HorizonEnd.ToDateTimeOffset();
        update.Result.SchedulePoints.Add(new SchedulePoint
        {
            WindowStart = Timestamp.FromDateTimeOffset(horizonStart),
            WindowEnd = Timestamp.FromDateTimeOffset(horizonEnd),
            TargetPowerKw = 10,
        });
        return update;
    }

    // Stub-Fallback der einen einfachen 1-Window-Schedule erzeugt mit
    // korrektem Kontext-Stempel — Validator passt durch.
    private sealed class SchedulingStubFallbackOptimizer : IFallbackScheduleOptimizer
    {
        public int CallCount { get; private set; }

        public Task<ScheduleOptimizationResult> OptimizeAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var horizonStart = request.HorizonStart.ToUniversalTime();
            var horizonEnd = request.HorizonEnd.ToUniversalTime();
            var schedule = new Schedule(
                assetId: request.AssetId,
                type: request.ScheduleType,
                marketBidArea: request.MarketBidArea,
                version: request.BaseScheduleVersion + 1,
                windows: new[]
                {
                    new ScheduleWindow(horizonStart, horizonEnd, TargetPowerKw: 5),
                });
            var run = new OptimizationRun(
                runId: Guid.NewGuid(),
                assetId: request.AssetId,
                solverName: "scripted-fallback-stub",
                status: OptimizationSolverStatus.Optimal,
                horizonStart: horizonStart,
                horizonEnd: horizonEnd,
                timeStep: request.TimeStep,
                objectiveValue: 0,
                objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
                constraintViolations: Array.Empty<string>(),
                warnings: Array.Empty<string>(),
                solverRuntime: TimeSpan.FromMilliseconds(1),
                terminationCode: "OPTIMAL",
                terminationDetail: null,
                createdAt: DateTimeOffset.UtcNow,
                inputs: request.Inputs,
                producedSchedule: new ScheduleReference(
                    request.AssetId, request.ScheduleType, request.BaseScheduleVersion + 1));
            return Task.FromResult(new ScheduleOptimizationResult(run, schedule));
        }
    }

    // Stub-Fallback der grundsätzlich wirft → Validator wird nicht
    // einmal aufgerufen, fall-closed-Pfad endet in no-valid-plan.
    private sealed class ThrowingFallbackOptimizer : IFallbackScheduleOptimizer
    {
        public int CallCount { get; private set; }

        public Task<ScheduleOptimizationResult> OptimizeAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException(
                "simulated-fallback-or-tools-crash");
        }
    }

    // Stub-Fallback der einen Plan mit fremden asset_id liefert —
    // löst FallbackContextMismatch im Plan-Validator aus.
    private sealed class ContextMismatchFallbackOptimizer : IFallbackScheduleOptimizer
    {
        public int CallCount { get; private set; }

        public Task<ScheduleOptimizationResult> OptimizeAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var horizonStart = request.HorizonStart.ToUniversalTime();
            var horizonEnd = request.HorizonEnd.ToUniversalTime();
            var schedule = new Schedule(
                assetId: "other-asset", // <-- mismatch
                type: request.ScheduleType,
                marketBidArea: request.MarketBidArea,
                version: request.BaseScheduleVersion + 1,
                windows: new[]
                {
                    new ScheduleWindow(horizonStart, horizonEnd, TargetPowerKw: 5),
                });
            var run = new OptimizationRun(
                runId: Guid.NewGuid(),
                assetId: "other-asset",
                solverName: "scripted-mismatch-fallback",
                status: OptimizationSolverStatus.Optimal,
                horizonStart: horizonStart,
                horizonEnd: horizonEnd,
                timeStep: request.TimeStep,
                objectiveValue: 0,
                objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
                constraintViolations: Array.Empty<string>(),
                warnings: Array.Empty<string>(),
                solverRuntime: TimeSpan.FromMilliseconds(1),
                terminationCode: "OPTIMAL",
                terminationDetail: null,
                createdAt: DateTimeOffset.UtcNow,
                inputs: request.Inputs,
                producedSchedule: new ScheduleReference(
                    "other-asset", request.ScheduleType, request.BaseScheduleVersion + 1));
            return Task.FromResult(new ScheduleOptimizationResult(run, schedule));
        }
    }
}
