using BatteryEms.Application.Markets;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class RegelleistungActivationStateStoreTests
{
    private static readonly DateTimeOffset At =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_store_returns_null_for_get_last()
    {
        var store = new InMemoryRegelleistungActivationStateStore();
        Assert.Null(store.GetLast());
    }

    [Fact]
    public void Record_outcome_then_get_last_returns_recorded_snapshot()
    {
        var store = new InMemoryRegelleistungActivationStateStore();
        var snap = new LastActivationSnapshot(
            "tso-source-1", "act-1", At, "accepted", DispatchRelevant: true,
            Details: "ok");

        store.RecordOutcome(snap);

        Assert.Same(snap, store.GetLast());
    }

    [Fact]
    public void Record_outcome_overwrites_prior_snapshot()
    {
        var store = new InMemoryRegelleistungActivationStateStore();
        var first = new LastActivationSnapshot(
            "src", "a", At, "accepted", true, "first");
        var second = new LastActivationSnapshot(
            "src", "b", At + TimeSpan.FromSeconds(1), "not-dispatch-relevant", false, "second");

        store.RecordOutcome(first);
        store.RecordOutcome(second);

        Assert.Same(second, store.GetLast());
    }

    [Fact]
    public void Record_null_throws()
    {
        var store = new InMemoryRegelleistungActivationStateStore();
        Assert.Throws<ArgumentNullException>(() => store.RecordOutcome(null!));
    }
}
