using BatteryEms.Application.Markets;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Application.Tests;

// RM-M4-03-E (plan §148) headline pins: end-to-end through the Sub-
// Slice C orchestrator + Sub-Slice D use-case + dispatch-source +
// Sub-Slice C optimizer integration. The profile tests inject a
// HealthyProductionPreconditionProvider so the test can drive the
// dispatch-relevant path without F-12 wired (D-03 test override).
public sealed class RegelleistungActivationProfileTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly BatteryAsset Asset = new(
        "asset-1", capacityKwh: 100,
        maxChargePowerKw: 50, maxDischargePowerKw: 50,
        minSocPercent: 10, maxSocPercent: 90,
        chargeEfficiency: 0.95, dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 10,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private static readonly BatteryTelemetry Telemetry = new(
        Timestamp: Now, AssetId: "asset-1",
        SocPercent: 50, SohPercent: 100,
        ActivePowerKw: 0, ReactivePowerKvar: 0,
        DcVoltage: 800, DcCurrent: 0,
        TemperatureCelsius: 25,
        Available: true, FaultStatus: "ok",
        DataQuality: DataQuality.Valid);

    private sealed class Pipeline
    {
        public RegelleistungOptions Options { get; }
        public InMemoryTimebaseHealthSource Timebase { get; } = new();
        public InMemoryActivationDispatchSource DispatchSource { get; } = new();
        public InMemoryActivationDedupeStore Dedupe { get; }
        public InMemoryRegelleistungActivationStateStore StateStore { get; } = new();
        public DefaultRegelleistungActivationUseCase UseCase { get; }
        public ScheduleFollowingDispatchOptimizer Optimizer { get; }
        public FakeClock Clock { get; } = new() { UtcNow = Now };

        public Pipeline(RegelleistungOptions? options = null)
        {
            Options = options ?? new RegelleistungOptions
            {
                ProductionActivationEnabled = true,
                ProductTrustEstablished = true,
            };
            Dedupe = new InMemoryActivationDedupeStore(Options, Clock);
            var validator = new ActivationValidator(Options, Dedupe, Clock);
            UseCase = new DefaultRegelleistungActivationUseCase(
                validator, Timebase, DispatchSource,
                new HealthyProductionPreconditionProvider(),
                StateStore, Options, Clock,
                NullLogger<DefaultRegelleistungActivationUseCase>.Instance);
            Optimizer = new ScheduleFollowingDispatchOptimizer(DispatchSource);
        }
    }

    private static RegelleistungActivation Activation(
        ReserveProduct product = ReserveProduct.Afrr,
        ReserveDirection direction = ReserveDirection.Up,
        double powerKw = 30,
        string sourceId = "tso-source-1",
        string activationId = "act-1",
        string payloadHash = "sha256:a",
        long sequenceNumber = 1)
        => new(
            sourceId, activationId, sequenceNumber,
            signalTimestampUtc: Now,
            product, direction, powerKw,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash);

    private static MarketCommitment DayAheadBinding(double powerKw) => new(
        Market: MarketType.DayAhead,
        MarketBidArea: "DE-LU",
        WindowStart: Now,
        WindowEnd: Now.AddHours(1),
        PowerKw: powerKw,
        Penalty: 0,
        BindingState: CommitmentBindingState.Binding);

    private static DispatchRequest Request(params MarketCommitment[] commitments) =>
        new("asset-1", Now, Asset, Telemetry, commitments);

    // Plan §148 aFRR-positive headline pin: Direction=Up, 15-min
    // window, eindeutige Leistungsinterpretation (PowerKw>0 = discharge),
    // konkurrierender DayAhead-Schedule (Rang 6) — Aktivierung gewinnt.
    [Fact]
    public async Task aFRR_positive_profile_dispatches_activation_setpoint_over_day_ahead_schedule()
    {
        var pipe = new Pipeline();

        var outcome = await pipe.UseCase.ReceiveAsync(
            Activation(direction: ReserveDirection.Up, powerKw: 30));

        Assert.True(outcome.DispatchRelevant);

        var dispatch = await pipe.Optimizer.OptimizeAsync(
            Request(DayAheadBinding(powerKw: 10)),
            CancellationToken.None);

        // Pin: setpoint is the activation's PowerKw (30, sign-mapped Up
        // = discharge = positive), NOT the schedule's PowerKw (10).
        Assert.True(dispatch.IsValid);
        Assert.Equal(30, dispatch.TargetActivePowerKw);
        Assert.Contains("regelleistung-activation-rank-3", dispatch.Reason, StringComparison.Ordinal);
    }

    // Plan §148 aFRR-negative headline pin: Direction=Down → charge sign.
    [Fact]
    public async Task aFRR_negative_profile_dispatches_charge_setpoint_over_day_ahead_schedule()
    {
        var pipe = new Pipeline();

        var outcome = await pipe.UseCase.ReceiveAsync(
            Activation(direction: ReserveDirection.Down, powerKw: 25));

        Assert.True(outcome.DispatchRelevant);

        var dispatch = await pipe.Optimizer.OptimizeAsync(
            Request(DayAheadBinding(powerKw: 10)),
            CancellationToken.None);

        // Pin: Down = charge = negative. Magnitude 25 → -25.
        Assert.Equal(-25, dispatch.TargetActivePowerKw);
    }

    // Plan §148 mFRR-Modellierbarkeit pin: validates + persists in the
    // dedupe tracker, but use-case marks not-dispatch-relevant even
    // with ProductionActivationEnabled=true and the gate fully green.
    [Fact]
    public async Task mFRR_modelable_validates_and_persists_but_never_dispatches()
    {
        var pipe = new Pipeline();

        var outcome = await pipe.UseCase.ReceiveAsync(
            Activation(product: ReserveProduct.Mfrr, direction: ReserveDirection.Up));

        Assert.False(outcome.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.NotDispatchRelevant, outcome.ReasonCode);
        // Pin: dispatch source did NOT receive the mFRR activation.
        Assert.Null(pipe.DispatchSource.GetActive(Now));
        // Pin: dedupe tracker DID accept and persist the mFRR
        // activation — it is modelable on the persistence side.
        Assert.Equal(1, pipe.Dedupe.CountForSource("tso-source-1"));
        // Pin: a replay of the same mFRR activation is idempotent
        // (proves the dedupe entry exists).
        var replay = await pipe.UseCase.ReceiveAsync(
            Activation(product: ReserveProduct.Mfrr, direction: ReserveDirection.Up));
        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, replay.ReasonCode);

        // Pin: optimizer falls through to schedule (no active mFRR
        // activation) when only mFRR has been received.
        var dispatch = await pipe.Optimizer.OptimizeAsync(
            Request(DayAheadBinding(powerKw: 10)),
            CancellationToken.None);
        Assert.Equal(10, dispatch.TargetActivePowerKw);
    }

    [Fact]
    public async Task Duplicate_replay_through_use_case_is_idempotent()
    {
        var pipe = new Pipeline();

        await pipe.UseCase.ReceiveAsync(Activation(payloadHash: "sha256:fixed"));
        var replay = await pipe.UseCase.ReceiveAsync(Activation(payloadHash: "sha256:fixed"));

        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, replay.ReasonCode);
        Assert.False(replay.DispatchRelevant);
    }

    [Fact]
    public async Task Conflicting_replay_through_use_case_is_dedupe_conflict()
    {
        var pipe = new Pipeline();

        var first = await pipe.UseCase.ReceiveAsync(Activation(payloadHash: "sha256:original"));
        Assert.True(first.DispatchRelevant);
        var firstHeld = pipe.DispatchSource.GetActive(Now);

        var conflict = await pipe.UseCase.ReceiveAsync(Activation(payloadHash: "sha256:tampered"));

        Assert.Equal(ActivationValidationReasons.DedupeConflict, conflict.ReasonCode);
        Assert.False(conflict.DispatchRelevant);
        // Pin: the original held activation is unchanged; a conflicting
        // replay does not overwrite the dispatch source.
        Assert.Same(firstHeld, pipe.DispatchSource.GetActive(Now));
    }
}
