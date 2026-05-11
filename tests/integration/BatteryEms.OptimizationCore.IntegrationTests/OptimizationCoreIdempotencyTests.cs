using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-C Step 1: Adapter-Integration des Idempotency-Stores.
// Pre-Sidecar TryBegin + Post-Sidecar Finalize; Duplicate-Detection
// ohne zweiten Sidecar-Roundtrip; späte Antwort markiert als
// late-response-ignored.
[Trait("Category", "Integration")]
[Collection("OptimizationCore Integration")]
public sealed class OptimizationCoreIdempotencyTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(4);
    private static readonly TimeSpan TimeStep = TimeSpan.FromHours(1);

    [Fact]
    public async Task First_optimize_creates_pending_then_finalizes_as_sidecar_committed()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<OptimalAlwaysSucceedsStub>();
        var store = new InMemoryOptimizationIdempotencyStore();
        var optimizer = BuildOptimizer(sidecar, store);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        var result = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);

        // Idempotency-Eintrag ist final mit SidecarCommitted.
        var allEntries = await CollectEntriesAsync(store, request);
        Assert.Single(allEntries);
        Assert.Equal(
            OptimizationTerminalState.SidecarCommitted,
            allEntries[0].TerminalState);
        Assert.Equal(result.Run.RunId, allEntries[0].RunId);
        Assert.Equal(result.ProducedSchedule!.Version, allEntries[0].ProducedVersion);
    }

    [Fact]
    public async Task Duplicate_optimize_with_same_inputs_skips_sidecar_call()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<OptimalAlwaysSucceedsStub>();
        var store = new InMemoryOptimizationIdempotencyStore();
        var optimizer = BuildOptimizer(sidecar, store);
        var request = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep);

        // Erster Optimize-Call: legt Pending an + finalisiert mit
        // SidecarCommitted.
        var first = await optimizer.OptimizeAsync(request, default);
        Assert.Equal(OptimizationSolverStatus.Optimal, first.Run.Status);
        Assert.NotNull(first.ProducedSchedule);

        // Zweiter Call mit identischen Inputs → Idempotency-Store
        // sieht existierenden finalen Eintrag → late-response-ignored
        // ohne zweiten Sidecar-Roundtrip.
        var second = await optimizer.OptimizeAsync(request, default);

        Assert.Equal(OptimizationSolverStatus.Failed, second.Run.Status);
        Assert.Null(second.ProducedSchedule);
        Assert.Equal("late-response-ignored", second.Run.TerminationCode);
    }

    [Fact]
    public async Task Different_request_inputs_get_different_request_id()
    {
        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<OptimalAlwaysSucceedsStub>();
        var store = new InMemoryOptimizationIdempotencyStore();
        var optimizer = BuildOptimizer(sidecar, store);

        // Verschiedene BaseScheduleVersion-Werte → verschiedene
        // canonical-form-Strings → verschiedene SHA-256-Hashes →
        // verschiedene request_ids; beide Calls landen mit Sidecar.
        var requestV0 = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep,
            baseScheduleVersion: 0);
        var requestV1 = Defaults.SampleRequest(HorizonStart, Horizon, TimeStep,
            baseScheduleVersion: 1);

        var r0 = await optimizer.OptimizeAsync(requestV0, default);
        var r1 = await optimizer.OptimizeAsync(requestV1, default);

        Assert.Equal(OptimizationSolverStatus.Optimal, r0.Run.Status);
        Assert.Equal(OptimizationSolverStatus.Optimal, r1.Run.Status);
        Assert.NotEqual(r0.Run.RunId, r1.Run.RunId);
        Assert.Equal(1, r0.ProducedSchedule!.Version);
        Assert.Equal(2, r1.ProducedSchedule!.Version);
    }

    private static async Task<IReadOnlyList<OptimizationIdempotencyEntry>> CollectEntriesAsync(
        InMemoryOptimizationIdempotencyStore store,
        ScheduleOptimizationRequest request)
    {
        // Single-asset, single-call → genau ein Eintrag mit der
        // canonical-form-derived request_id. Wir kennen die ID nicht
        // direkt; brute-force durch Reflexion ist Test-fragil — also
        // einfach den Store-State über einen weiteren TryBegin-Probe
        // abfragen.
        var canonicalProbeId = ComputeCanonicalProbeId(request);
        var entry = await store.ReadAsync(canonicalProbeId, default);
        return entry is null
            ? Array.Empty<OptimizationIdempotencyEntry>()
            : new[] { entry };
    }

    // Replica der private ComputeRequestId-Logik im Adapter
    // (canonical form + SHA-256-prefix-as-Guid); muss synchron bleiben.
    private static string ComputeCanonicalProbeId(ScheduleOptimizationRequest r)
    {
        var canonical = string.Join('|',
            r.AssetId,
            r.ScheduleType.ToString(),
            r.HorizonStart.ToUniversalTime().ToString("O"),
            r.HorizonEnd.ToUniversalTime().ToString("O"),
            r.TimeStep.ToString("c"),
            r.BaseScheduleVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            r.MarketBidArea);
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16).ToArray()).ToString("D");
    }

    private static OptimizationCoreScheduleOptimizer BuildOptimizer(
        EmbeddedOptimizationCoreSidecar sidecar,
        IOptimizationIdempotencyStore store)
    {
        var options = Defaults.ForHilSimulator(sidecar.Endpoint);
        var client = new OptimizationCoreClient(options);
        return new OptimizationCoreScheduleOptimizer(
            client,
            options,
            store,
            new Defaults.FixedClock(),
            NullLogger<OptimizationCoreScheduleOptimizer>.Instance);
    }
}
