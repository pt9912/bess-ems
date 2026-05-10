using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryActivationDispatchSourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static RegelleistungActivation Activation(
        string sourceId = "tso-source-1",
        string activationId = "act-1",
        long sequenceNumber = 1,
        DateTimeOffset? signalTimestamp = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null)
        => new(
            sourceId, activationId, sequenceNumber,
            signalTimestamp ?? Now,
            ReserveProduct.Afrr, ReserveDirection.Up,
            powerKw: 25,
            validFrom: validFrom ?? Now,
            validUntil: validUntil ?? Now + TimeSpan.FromMinutes(15),
            payloadHash: "sha256:abc");

    [Fact]
    public void Empty_source_returns_null_at_get_active()
    {
        var source = new InMemoryActivationDispatchSource();

        Assert.Null(source.GetActive(Now));
    }

    [Fact]
    public void Submit_then_get_active_returns_held_activation()
    {
        var source = new InMemoryActivationDispatchSource();
        var act = Activation();

        source.Submit(act);

        Assert.Same(act, source.GetActive(Now));
    }

    [Fact]
    public void Get_active_returns_null_when_now_is_past_valid_until()
    {
        var source = new InMemoryActivationDispatchSource();
        source.Submit(Activation(validUntil: Now + TimeSpan.FromMinutes(5)));

        Assert.Null(source.GetActive(Now + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Get_active_returns_null_at_exactly_valid_until_half_open()
    {
        var source = new InMemoryActivationDispatchSource();
        var validUntil = Now + TimeSpan.FromMinutes(5);
        source.Submit(Activation(validUntil: validUntil));

        Assert.Null(source.GetActive(validUntil));
    }

    [Fact]
    public void Get_active_returns_null_when_now_is_before_valid_from()
    {
        var source = new InMemoryActivationDispatchSource();
        var validFrom = Now + TimeSpan.FromMinutes(1);
        source.Submit(Activation(
            validFrom: validFrom,
            validUntil: validFrom + TimeSpan.FromMinutes(10)));

        Assert.Null(source.GetActive(Now));
    }

    // Plan §148 tiebreak pin: higher SequenceNumber wins.
    [Fact]
    public void Submit_with_higher_sequence_number_replaces_held()
    {
        var source = new InMemoryActivationDispatchSource();
        var lower = Activation(sequenceNumber: 5);
        var higher = Activation(sequenceNumber: 7);

        source.Submit(lower);
        source.Submit(higher);

        Assert.Same(higher, source.GetActive(Now));
    }

    [Fact]
    public void Submit_with_lower_sequence_number_keeps_held()
    {
        var source = new InMemoryActivationDispatchSource();
        var higher = Activation(sequenceNumber: 7);
        var lower = Activation(sequenceNumber: 5);

        source.Submit(higher);
        source.Submit(lower);

        Assert.Same(higher, source.GetActive(Now));
    }

    // Plan §148 tiebreak: equal SequenceNumber → newer SignalTimestampUtc wins.
    [Fact]
    public void Equal_sequence_newer_timestamp_replaces_held()
    {
        var source = new InMemoryActivationDispatchSource();
        var older = Activation(sequenceNumber: 5, signalTimestamp: Now);
        var newer = Activation(sequenceNumber: 5, signalTimestamp: Now + TimeSpan.FromSeconds(1));

        source.Submit(older);
        source.Submit(newer);

        Assert.Same(newer, source.GetActive(Now));
    }

    // Plan §148 tiebreak: equal seq + timestamp → lex-smaller (source_id, activation_id) wins.
    [Fact]
    public void Equal_seq_and_timestamp_lex_smaller_source_id_replaces_held()
    {
        var source = new InMemoryActivationDispatchSource();
        var b = Activation(sourceId: "source-b");
        var a = Activation(sourceId: "source-a");

        source.Submit(b);
        source.Submit(a);

        Assert.Same(a, source.GetActive(Now));
    }

    [Fact]
    public void Clear_removes_held_activation()
    {
        var source = new InMemoryActivationDispatchSource();
        source.Submit(Activation());

        source.Clear();

        Assert.Null(source.GetActive(Now));
    }

    [Fact]
    public void Submit_null_throws()
    {
        var source = new InMemoryActivationDispatchSource();
        Assert.Throws<ArgumentNullException>(() => source.Submit(null!));
    }

    [Fact]
    public void NoOp_source_never_holds_an_activation()
    {
        var source = new NoOpActivationDispatchSource();
        source.Submit(Activation());

        Assert.Null(source.GetActive(Now));
    }

    [Fact]
    public void NoOp_source_clear_is_a_noop()
    {
        var source = new NoOpActivationDispatchSource();
        source.Clear();
        Assert.Null(source.GetActive(Now));
    }
}
