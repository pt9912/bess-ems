using BatteryEms.Application.Optimization;
using Xunit;

namespace BatteryEms.Application.Tests;

// Plan-RM-M5-01-C: Idempotency-Store CAS-Semantik gepinnt für den
// In-Memory-Default. Dapper-Pendant landet in der Persistence-Schicht-
// Folge-Slice mit denselben Pins via Postgres-Fixture.
public sealed class InMemoryOptimizationIdempotencyStoreTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryBegin_creates_new_pending_entry_when_request_id_unknown()
    {
        var store = new InMemoryOptimizationIdempotencyStore();

        var result = await store.TryBeginAsync("req-1", T0, default);

        Assert.True(result.IsNewlyCreated);
        Assert.Equal(OptimizationTerminalState.Pending, result.Entry.TerminalState);
        Assert.Equal("req-1", result.Entry.RequestId);
        Assert.Equal(T0, result.Entry.CreatedAt);
        Assert.Null(result.Entry.CommittedAt);
        Assert.False(result.Entry.IsFinal);
    }

    [Fact]
    public async Task TryBegin_returns_existing_pending_entry_on_duplicate_call()
    {
        var store = new InMemoryOptimizationIdempotencyStore();
        await store.TryBeginAsync("req-1", T0, default);

        var duplicate = await store.TryBeginAsync("req-1", T0.AddSeconds(1), default);

        Assert.False(duplicate.IsNewlyCreated);
        Assert.Equal(T0, duplicate.Entry.CreatedAt); // original CreatedAt
    }

    [Fact]
    public async Task TryFinalize_transitions_pending_to_terminal_state_once()
    {
        var store = new InMemoryOptimizationIdempotencyStore();
        await store.TryBeginAsync("req-1", T0, default);

        var runId = Guid.NewGuid();
        var first = await store.TryFinalizeAsync(
            "req-1", OptimizationTerminalState.SidecarCommitted,
            "sidecar-committed", runId, 5, T0.AddSeconds(1), default);

        Assert.True(first);
        var entry = await store.ReadAsync("req-1", default);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.SidecarCommitted, entry!.TerminalState);
        Assert.Equal(runId, entry.RunId);
        Assert.Equal(5, entry.ProducedVersion);
        Assert.True(entry.IsFinal);
    }

    [Fact]
    public async Task TryFinalize_returns_false_on_already_finalized_entry()
    {
        var store = new InMemoryOptimizationIdempotencyStore();
        await store.TryBeginAsync("req-1", T0, default);
        await store.TryFinalizeAsync(
            "req-1", OptimizationTerminalState.SidecarCommitted,
            "sidecar-committed", Guid.NewGuid(), 5, T0.AddSeconds(1), default);

        // Second finalize attempt with a different terminal state.
        var second = await store.TryFinalizeAsync(
            "req-1", OptimizationTerminalState.FailedNoActivation,
            "should-not-overwrite", null, null, T0.AddSeconds(2), default);

        Assert.False(second);
        var entry = await store.ReadAsync("req-1", default);
        // Original Terminal-State bleibt erhalten.
        Assert.Equal(OptimizationTerminalState.SidecarCommitted, entry!.TerminalState);
        Assert.Equal("sidecar-committed", entry.TerminalReason);
    }

    [Fact]
    public async Task TryFinalize_returns_false_for_unknown_request_id()
    {
        var store = new InMemoryOptimizationIdempotencyStore();

        var result = await store.TryFinalizeAsync(
            "unknown", OptimizationTerminalState.SidecarCommitted,
            "test", Guid.NewGuid(), 1, T0, default);

        Assert.False(result);
    }

    [Fact]
    public async Task TryFinalize_with_pending_state_throws()
    {
        var store = new InMemoryOptimizationIdempotencyStore();
        await store.TryBeginAsync("req-1", T0, default);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.TryFinalizeAsync(
                "req-1", OptimizationTerminalState.Pending,
                "test", null, null, T0, default));
    }

    [Fact]
    public async Task Read_returns_null_for_unknown_request_id()
    {
        var store = new InMemoryOptimizationIdempotencyStore();

        var entry = await store.ReadAsync("unknown", default);

        Assert.Null(entry);
    }

    [Fact]
    public async Task Concurrent_TryBegin_only_creates_one_entry()
    {
        var store = new InMemoryOptimizationIdempotencyStore();

        // Race: 20 parallele TryBegin-Calls für dieselbe request_id.
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => store.TryBeginAsync("req-race", T0, default))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Exakt einer hat IsNewlyCreated=true; alle anderen finden
        // den existierenden Eintrag.
        Assert.Equal(1, results.Count(r => r.IsNewlyCreated));
        Assert.Equal(19, results.Count(r => !r.IsNewlyCreated));
    }

    [Fact]
    public async Task Concurrent_TryFinalize_only_one_winner()
    {
        var store = new InMemoryOptimizationIdempotencyStore();
        await store.TryBeginAsync("req-1", T0, default);

        var tasks = Enumerable.Range(0, 10).Select(i =>
            store.TryFinalizeAsync(
                "req-1",
                OptimizationTerminalState.SidecarCommitted,
                $"reason-{i}",
                Guid.NewGuid(),
                i,
                T0.AddSeconds(i + 1),
                default)).ToArray();
        var results = await Task.WhenAll(tasks);

        // Genau ein Caller gewinnt das CAS-Race; alle anderen sehen
        // den schon-finalen Eintrag.
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(9, results.Count(r => !r));
    }
}
