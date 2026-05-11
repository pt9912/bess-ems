using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-D §4 Sub-Slice-D: 4 Mixed-Version-Compat-Pins.
// Decken die plan-RM-M5 §Contract-Versionen Und Rollout-Matrix:
//   (i)   Worker 1.0 ↔ Sidecar 1.0 (compat)          → Optimize-Success
//   (ii)  Worker 1.0 ↔ Sidecar 0.5 (sidecar-too-old) → contract-incompatible-Fallback, kein Optimize-Request
//   (iii) Worker 1.0 ↔ Sidecar 2.0 (sidecar-too-new) → contract-incompatible-Fallback, kein Optimize-Request
//   (iv)  Worker erwartet `has-usable-solution`, Sidecar liefert features=[] → contract-incompatible-Fallback
//
// Adapter-Logik: `OptimizationCoreScheduleOptimizer.EnsureContractCompatibleAsync`
// macht Health- + Version-Probe einmal pro Lifetime und wirft eine
// `ContractIncompatibleException` bei Range-Miss oder fehlendem
// Pflicht-Feature. Der äußere Catch baut dann einen Failed-Run mit
// `terminationCode="contract-incompatible"` und finalisiert den
// Idempotency-Eintrag als `FailedNoActivation` (kein Sidecar-Optimize-
// Call findet statt — der StatusMapper.ClassifyContractIncompatible
// liefert PersistSchedule=false).
[Trait("Category", "Integration")]
[Collection("OptimizationCore Integration")]
public sealed class OptimizationCoreMixedVersionTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(4);
    private static readonly TimeSpan TimeStep = TimeSpan.FromHours(1);

    // (i) Compat-Baseline: identische Versionen → Sidecar wird
    // tatsächlich angesprochen, Optimize gibt eine usable Lösung
    // zurück. Pin existiert hier als symmetrische Baseline zur
    // Mixed-Version-Matrix (die Happy-Path-Pins in den Roundtrip-Tests
    // verlassen sich auf die Default-Version aus `ScriptableOutcomeStub`).
    [Fact]
    public async Task Worker_1_0_against_sidecar_1_0_optimizes_successfully()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        var stub = sidecar.GetService<ScriptableOutcomeStub>();
        stub.SetVersion("1.0.0", min: "1.0.0", max: "1.0.0", "has-usable-solution");
        stub.EnqueueOptimize((req, stream, _) =>
            stream.WriteAsync(BuildOptimalUpdate(req)));

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
    }

    // (ii) Sidecar-too-old: Sidecar reportet contract_version=0.5 mit
    // Range [0.5, 0.5]. Worker erwartet 1.0.0 → liegt NICHT im
    // Sidecar-Support-Range → `ContractIncompatibleException` →
    // Failed-Run mit terminationCode `contract-incompatible`. Optimize
    // wird gar nicht aufgerufen (keine Enqueue nötig).
    [Fact]
    public async Task Worker_1_0_against_sidecar_0_5_returns_contract_incompatible()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>()
            .SetVersion("0.5.0", min: "0.5.0", max: "0.5.0", "has-usable-solution");

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("contract-incompatible", result.Run.TerminationCode);
        Assert.NotNull(result.Run.TerminationDetail);
        Assert.Contains("contract-version-mismatch",
            result.Run.TerminationDetail, StringComparison.Ordinal);
    }

    // (iii) Sidecar-too-new: Sidecar reportet contract_version=2.0 mit
    // Range [2.0, 2.0]. Worker erwartet 1.0.0 → out-of-range
    // (zu-niedrig) → `ContractIncompatibleException` → Failed-Run.
    // Auch hier KEIN Optimize-Call.
    [Fact]
    public async Task Worker_1_0_against_sidecar_2_0_min_returns_contract_incompatible()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        sidecar.GetService<ScriptableOutcomeStub>()
            .SetVersion("2.0.0", min: "2.0.0", max: "2.0.0", "has-usable-solution");

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("contract-incompatible", result.Run.TerminationCode);
        Assert.NotNull(result.Run.TerminationDetail);
        Assert.Contains("contract-version-mismatch",
            result.Run.TerminationDetail, StringComparison.Ordinal);
    }

    // (iv) Pflicht-Feature-Flag-Lücke: Sidecar in der unterstützten
    // Versions-Range, aber liefert ein leeres features-Array. Der
    // Worker hat `RequiredFeatures = ["has-usable-solution"]` (siehe
    // OptimizationCoreOptions-Default + Defaults.ForHilSimulator) und
    // reklassifiziert das als `contract-incompatible` ohne Optimize-Call.
    [Fact]
    public async Task Worker_required_feature_missing_returns_contract_incompatible()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<ScriptableOutcomeStub>();
        // Versionen passen — features ist bewusst leer.
        sidecar.GetService<ScriptableOutcomeStub>()
            .SetVersion("1.0.0", min: "1.0.0", max: "1.0.0");

        var optimizer = BuildOptimizer(sidecar);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
        Assert.Equal("contract-incompatible", result.Run.TerminationCode);
        Assert.NotNull(result.Run.TerminationDetail);
        Assert.Contains("required-feature-missing",
            result.Run.TerminationDetail, StringComparison.Ordinal);
        Assert.Contains("has-usable-solution",
            result.Run.TerminationDetail, StringComparison.Ordinal);
    }

    private static OptimizationCoreScheduleOptimizer BuildOptimizer(
        EmbeddedOptimizationCoreSidecar sidecar)
    {
        var options = Defaults.ForHilSimulator(sidecar.Endpoint);
        var client = new OptimizationCoreClient(options);
        return new OptimizationCoreScheduleOptimizer(
            client,
            options,
            new InMemoryOptimizationIdempotencyStore(),
            new Defaults.FixedClock(),
            NullLogger<OptimizationCoreScheduleOptimizer>.Instance);
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
                SolverName = "scripted-compat-stub",
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
}
