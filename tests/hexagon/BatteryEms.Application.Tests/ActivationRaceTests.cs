using BatteryEms.Application.Markets;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Application.Tests;

// RM-M4-03-E race + tiebreak pins (plan §148). The dispatch source is
// the single-slot holder; competing submissions resolve via the
// tiebreak (higher SequenceNumber > newer SignalTimestampUtc >
// lex-smaller (source_id, activation_id)). Equal full tuple cannot
// arise between distinct identities — same identity collapses to
// dedupe-replay or dedupe-conflict at the store level.
public sealed class ActivationRaceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Pipeline
    {
        public InMemoryActivationDispatchSource DispatchSource { get; } = new();
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
                validator, new InMemoryTimebaseHealthSource(), DispatchSource,
                new HealthyProductionPreconditionProvider(),
                new InMemoryRegelleistungActivationStateStore(),
                options, clock,
                NullLogger<DefaultRegelleistungActivationUseCase>.Instance);
        }
    }

    private static RegelleistungActivation Activation(
        string sourceId, string activationId,
        long sequenceNumber = 1,
        DateTimeOffset? signalTimestamp = null,
        string payloadHash = "sha256:a")
        => new(
            sourceId, activationId, sequenceNumber,
            signalTimestamp ?? Now,
            ReserveProduct.Afrr, ReserveDirection.Up,
            powerKw: 30,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash);

    [Fact]
    public async Task Two_concurrent_sources_higher_sequence_number_wins_dispatch()
    {
        var pipe = new Pipeline();
        var lower = Activation("source-a", "act-1", sequenceNumber: 5);
        var higher = Activation("source-b", "act-2", sequenceNumber: 7);

        await pipe.UseCase.ReceiveAsync(lower);
        await pipe.UseCase.ReceiveAsync(higher);

        var held = pipe.DispatchSource.GetActive(Now);
        Assert.NotNull(held);
        Assert.Equal("source-b", held!.SourceId);
        Assert.Equal(7, held.SequenceNumber);
    }

    [Fact]
    public async Task Two_concurrent_sources_lower_sequence_arrives_second_does_not_replace()
    {
        var pipe = new Pipeline();
        var higher = Activation("source-b", "act-2", sequenceNumber: 7);
        var lower = Activation("source-a", "act-1", sequenceNumber: 5);

        await pipe.UseCase.ReceiveAsync(higher);
        await pipe.UseCase.ReceiveAsync(lower);

        var held = pipe.DispatchSource.GetActive(Now);
        Assert.Equal(7, held!.SequenceNumber);
    }

    // Plan §148 tiebreak step 2: equal SequenceNumber → newer
    // SignalTimestampUtc wins.
    [Fact]
    public async Task Equal_sequence_number_newer_timestamp_wins()
    {
        var pipe = new Pipeline();
        var older = Activation("source-a", "act-1",
            sequenceNumber: 5, signalTimestamp: Now);
        var newer = Activation("source-b", "act-2",
            sequenceNumber: 5, signalTimestamp: Now + TimeSpan.FromMilliseconds(100));

        await pipe.UseCase.ReceiveAsync(older);
        await pipe.UseCase.ReceiveAsync(newer);

        var held = pipe.DispatchSource.GetActive(Now);
        Assert.Equal("source-b", held!.SourceId);
    }

    // Plan §148 tiebreak step 3: equal sequence + timestamp →
    // lex-smaller (source_id, activation_id) wins.
    [Fact]
    public async Task Equal_sequence_and_timestamp_lex_smaller_source_id_wins()
    {
        var pipe = new Pipeline();
        var b = Activation("source-zeta", "act-x",
            sequenceNumber: 5, signalTimestamp: Now);
        var a = Activation("source-alpha", "act-x",
            sequenceNumber: 5, signalTimestamp: Now);

        await pipe.UseCase.ReceiveAsync(b);
        await pipe.UseCase.ReceiveAsync(a);

        var held = pipe.DispatchSource.GetActive(Now);
        Assert.Equal("source-alpha", held!.SourceId);
    }

    // Plan §148 same-identity-replay pin: same (source_id, activation_id)
    // + identical payload arriving twice — the second is replay-
    // idempotent at the dedupe layer; the use-case does not re-submit
    // to the dispatch source. The held activation is the FIRST instance
    // (replay does not overwrite).
    [Fact]
    public async Task Duplicate_identity_with_identical_payload_is_replay_idempotent()
    {
        var pipe = new Pipeline();
        var first = Activation("source-a", "act-1", sequenceNumber: 5, payloadHash: "sha256:p");
        var replay = Activation("source-a", "act-1", sequenceNumber: 5, payloadHash: "sha256:p");

        var firstOutcome = await pipe.UseCase.ReceiveAsync(first);
        Assert.True(firstOutcome.DispatchRelevant);
        var replayOutcome = await pipe.UseCase.ReceiveAsync(replay);

        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, replayOutcome.ReasonCode);
        Assert.False(replayOutcome.DispatchRelevant);
        // Pin: same dispatch entry (the first one); not replaced.
        Assert.Same(first, pipe.DispatchSource.GetActive(Now));
    }
}
