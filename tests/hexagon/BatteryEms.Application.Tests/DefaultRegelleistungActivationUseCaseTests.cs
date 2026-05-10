using BatteryEms.Application.Markets;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class DefaultRegelleistungActivationUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static RegelleistungActivation Activation(
        ReserveProduct product = ReserveProduct.Afrr,
        ReserveDirection direction = ReserveDirection.Up,
        string sourceId = "tso-source-1",
        string activationId = "act-1",
        string payloadHash = "sha256:abc")
        => new(
            sourceId, activationId, sequenceNumber: 1,
            signalTimestampUtc: Now,
            product, direction,
            powerKw: 25,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash);

    private sealed class TestHarness
    {
        public RegelleistungOptions Options { get; set; } = new();
        public InMemoryTimebaseHealthSource Timebase { get; } = new();
        public InMemoryActivationDispatchSource DispatchSource { get; } = new();
        public InMemoryActivationDedupeStore Dedupe { get; }
        public InMemoryRegelleistungActivationStateStore StateStore { get; } = new();
        public IProductionPreconditionProvider Preconditions { get; set; } =
            new HealthyProductionPreconditionProvider();
        public FakeClock Clock { get; } = new() { UtcNow = Now };

        public TestHarness()
        {
            Dedupe = new InMemoryActivationDedupeStore(Options, Clock);
        }

        public DefaultRegelleistungActivationUseCase Build()
        {
            var validator = new ActivationValidator(Options, Dedupe, Clock);
            return new DefaultRegelleistungActivationUseCase(
                validator, Timebase, DispatchSource, Preconditions, StateStore, Options, Clock,
                NullLogger<DefaultRegelleistungActivationUseCase>.Instance);
        }
    }

    [Fact]
    public async Task Production_disabled_returns_not_dispatch_relevant()
    {
        var harness = new TestHarness { Options = new RegelleistungOptions { ProductionActivationEnabled = false } };
        var useCase = harness.Build();

        var outcome = await useCase.ReceiveAsync(Activation());

        Assert.Equal(ActivationValidationReasons.NotDispatchRelevant, outcome.ReasonCode);
        Assert.False(outcome.DispatchRelevant);
        Assert.Null(harness.DispatchSource.GetActive(Now));
    }

    [Fact]
    public async Task Production_enabled_with_healthy_provider_dispatches_afrr()
    {
        var harness = new TestHarness
        {
            Options = new RegelleistungOptions
            {
                ProductionActivationEnabled = true,
                ProductTrustEstablished = true,
            },
        };
        var useCase = harness.Build();

        var outcome = await useCase.ReceiveAsync(Activation());

        Assert.True(outcome.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.Accepted, outcome.ReasonCode);
        var held = harness.DispatchSource.GetActive(Now);
        Assert.NotNull(held);
        Assert.Equal("act-1", held!.ActivationId);
    }

    // Plan §147 / D-05: mFRR is modelable but never dispatched in M4
    // even with the production gate green.
    [Fact]
    public async Task Mfrr_activation_is_never_dispatched_even_when_gate_green()
    {
        var harness = new TestHarness
        {
            Options = new RegelleistungOptions
            {
                ProductionActivationEnabled = true,
                ProductTrustEstablished = true,
            },
        };
        var useCase = harness.Build();

        var outcome = await useCase.ReceiveAsync(
            Activation(product: ReserveProduct.Mfrr, direction: ReserveDirection.Up));

        Assert.False(outcome.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.NotDispatchRelevant, outcome.ReasonCode);
        Assert.Null(harness.DispatchSource.GetActive(Now));
    }

    // Plan §147 / D-03: production-code provider stays fail-closed on
    // security-profile until F-12 wires a real signal.
    [Fact]
    public async Task Production_enabled_with_default_provider_fails_on_security_profile()
    {
        var harness = new TestHarness
        {
            Options = new RegelleistungOptions
            {
                ProductionActivationEnabled = true,
                ProductTrustEstablished = true,
            },
            Preconditions = new DefaultProductionPreconditionProvider(
                new InMemoryTimebaseHealthSource(),
                new InMemoryActivationDedupeStore(new RegelleistungOptions(), new FakeClock())),
        };
        var useCase = harness.Build();

        var outcome = await useCase.ReceiveAsync(Activation());

        Assert.False(outcome.DispatchRelevant);
        Assert.Equal(
            ActivationValidationReasons.SecurityProfileEnforcementNotWired,
            outcome.ReasonCode);
    }

    [Fact]
    public async Task Validation_failure_short_circuits_and_records_audit()
    {
        var harness = new TestHarness();
        // Make the validation fail by putting timebase into Degraded.
        harness.Timebase.Observe(true);
        harness.Timebase.Observe(true);
        harness.Timebase.Observe(true);
        var useCase = harness.Build();

        var outcome = await useCase.ReceiveAsync(Activation());

        Assert.False(outcome.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.TimebaseDegraded, outcome.ReasonCode);

        var snap = harness.StateStore.GetLast();
        Assert.NotNull(snap);
        Assert.Equal("act-1", snap!.ActivationId);
        Assert.Equal(ActivationValidationReasons.TimebaseDegraded, snap.ReasonCode);
        Assert.False(snap.DispatchRelevant);
    }

    [Fact]
    public async Task Successful_outcome_records_audit_with_dispatch_relevant_true()
    {
        var harness = new TestHarness
        {
            Options = new RegelleistungOptions
            {
                ProductionActivationEnabled = true,
                ProductTrustEstablished = true,
            },
        };
        var useCase = harness.Build();

        await useCase.ReceiveAsync(Activation());

        var snap = harness.StateStore.GetLast();
        Assert.NotNull(snap);
        Assert.True(snap!.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.Accepted, snap.ReasonCode);
        Assert.Equal(Now, snap.ReceivedAt);
    }

    [Fact]
    public async Task Schema_invalid_propagates_to_outcome()
    {
        var harness = new TestHarness();
        var useCase = harness.Build();

        var outcome = await useCase.ReceiveAsync(Activation(sourceId: "tso source 1"));

        Assert.False(outcome.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.SchemaInvalid, outcome.ReasonCode);
    }

    [Fact]
    public async Task Replay_idempotent_does_not_resubmit_to_dispatch_source()
    {
        var harness = new TestHarness
        {
            Options = new RegelleistungOptions
            {
                ProductionActivationEnabled = true,
                ProductTrustEstablished = true,
            },
        };
        var useCase = harness.Build();

        await useCase.ReceiveAsync(Activation());
        harness.DispatchSource.Clear();

        var replay = await useCase.ReceiveAsync(Activation());

        Assert.False(replay.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, replay.ReasonCode);
        Assert.Null(harness.DispatchSource.GetActive(Now));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var harness = new TestHarness();
        var useCase = harness.Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ReceiveAsync(Activation(), cts.Token));
    }

    [Fact]
    public void Constructor_null_args_throw()
    {
        var harness = new TestHarness();
        var validator = new ActivationValidator(harness.Options, harness.Dedupe, harness.Clock);
        var logger = NullLogger<DefaultRegelleistungActivationUseCase>.Instance;

        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            null!, harness.Timebase, harness.DispatchSource, harness.Preconditions, harness.StateStore, harness.Options, harness.Clock, logger));
        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            validator, null!, harness.DispatchSource, harness.Preconditions, harness.StateStore, harness.Options, harness.Clock, logger));
        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            validator, harness.Timebase, null!, harness.Preconditions, harness.StateStore, harness.Options, harness.Clock, logger));
        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            validator, harness.Timebase, harness.DispatchSource, null!, harness.StateStore, harness.Options, harness.Clock, logger));
        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            validator, harness.Timebase, harness.DispatchSource, harness.Preconditions, null!, harness.Options, harness.Clock, logger));
        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            validator, harness.Timebase, harness.DispatchSource, harness.Preconditions, harness.StateStore, null!, harness.Clock, logger));
        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            validator, harness.Timebase, harness.DispatchSource, harness.Preconditions, harness.StateStore, harness.Options, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new DefaultRegelleistungActivationUseCase(
            validator, harness.Timebase, harness.DispatchSource, harness.Preconditions, harness.StateStore, harness.Options, harness.Clock, null!));
    }
}
