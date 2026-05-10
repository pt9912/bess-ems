using BatteryEms.Application.Markets;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Application.Tests;

// RM-M4-03-E pipeline debounce pin (plan §148): the
// ITimebaseHealthSource is fed by the cycle owner with violation /
// stable observations; the activation pipeline reads .Current per
// reception. After three violations (within the 10-cycle window) the
// state is Degraded and every reception fails timebase-degraded.
// Five consecutive stable observations recover the state and the next
// reception is admitted again. This test wires the cycle-side
// observation explicitly via the source's Observe method — the real
// cycle wiring is beyond M4-03 scope.
public sealed class RegelleistungTimebaseDebouncePipelineTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Pipeline
    {
        public InMemoryTimebaseHealthSource Timebase { get; } = new();
        public DefaultRegelleistungActivationUseCase UseCase { get; }

        public Pipeline()
        {
            var clock = new FakeClock { UtcNow = Now };
            var options = new RegelleistungOptions
            {
                ProductionActivationEnabled = true,
                ProductTrustEstablished = true,
            };
            var dedupe = new InMemoryActivationDedupeStore(options, clock);
            var validator = new ActivationValidator(options, dedupe, clock);
            UseCase = new DefaultRegelleistungActivationUseCase(
                validator, Timebase,
                new InMemoryActivationDispatchSource(),
                new HealthyProductionPreconditionProvider(),
                new InMemoryRegelleistungActivationStateStore(),
                options, clock,
                NullLogger<DefaultRegelleistungActivationUseCase>.Instance);
        }
    }

    private static RegelleistungActivation Activation(string activationId)
        => new(
            sourceId: "tso-source-1",
            activationId,
            sequenceNumber: 1,
            signalTimestampUtc: Now,
            product: ReserveProduct.Afrr,
            direction: ReserveDirection.Up,
            powerKw: 25,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash: "sha256:" + activationId);

    [Fact]
    public async Task Three_violations_in_window_block_next_activation_with_timebase_degraded()
    {
        var pipe = new Pipeline();

        // Cycle owner reports three stale ticks.
        pipe.Timebase.Observe(violationThisCycle: true);
        pipe.Timebase.Observe(violationThisCycle: true);
        pipe.Timebase.Observe(violationThisCycle: true);

        var outcome = await pipe.UseCase.ReceiveAsync(Activation("act-1"));

        Assert.False(outcome.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.TimebaseDegraded, outcome.ReasonCode);
    }

    [Fact]
    public async Task Five_stable_cycles_recover_and_next_activation_is_accepted()
    {
        var pipe = new Pipeline();

        pipe.Timebase.Observe(true);
        pipe.Timebase.Observe(true);
        pipe.Timebase.Observe(true);
        // Confirm we're degraded — a reception now is gated.
        var degradedOutcome = await pipe.UseCase.ReceiveAsync(Activation("act-pre"));
        Assert.Equal(ActivationValidationReasons.TimebaseDegraded, degradedOutcome.ReasonCode);

        // Five consecutive stable cycles recover the timebase.
        for (var i = 0; i < 5; i++)
        {
            pipe.Timebase.Observe(false);
        }

        var afterRecover = await pipe.UseCase.ReceiveAsync(Activation("act-post"));

        Assert.True(afterRecover.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.Accepted, afterRecover.ReasonCode);
    }

    [Fact]
    public async Task Explicit_recover_after_degraded_admits_next_activation()
    {
        var pipe = new Pipeline();
        pipe.Timebase.Observe(true);
        pipe.Timebase.Observe(true);
        pipe.Timebase.Observe(true);

        pipe.Timebase.Recover();

        var outcome = await pipe.UseCase.ReceiveAsync(Activation("act-1"));
        Assert.True(outcome.DispatchRelevant);
    }
}
