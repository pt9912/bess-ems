using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryActivationDedupeStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static RegelleistungActivation Activation(
        string sourceId = "tso-source-1",
        string activationId = "act-1",
        long sequenceNumber = 1,
        string payloadHash = "sha256:aaa",
        DateTimeOffset? signalTimestamp = null)
        => new(
            sourceId,
            activationId,
            sequenceNumber,
            signalTimestamp ?? Now,
            ReserveProduct.Afrr,
            ReserveDirection.Up,
            powerKw: 25,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash);

    private static (InMemoryActivationDedupeStore Store, FakeClock Clock) BuildStore(
        RegelleistungOptions? options = null)
    {
        var clock = new FakeClock { UtcNow = Now };
        return (new InMemoryActivationDedupeStore(options ?? new RegelleistungOptions(), clock), clock);
    }

    [Fact]
    public async Task First_accept_returns_accepted()
    {
        var (store, _) = BuildStore();

        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.Accepted, result);
    }

    [Fact]
    public async Task Replay_with_same_payload_hash_is_idempotent()
    {
        var (store, _) = BuildStore();
        await store.TryAcceptAsync(Activation(payloadHash: "sha256:abc"));

        var result = await store.TryAcceptAsync(Activation(payloadHash: "sha256:abc"));

        Assert.Equal(AcceptResult.ReplayIdempotent, result);
    }

    [Fact]
    public async Task Same_identity_with_different_payload_is_dedupe_conflict()
    {
        var (store, _) = BuildStore();
        await store.TryAcceptAsync(Activation(payloadHash: "sha256:original"));

        var result = await store.TryAcceptAsync(Activation(payloadHash: "sha256:tampered"));

        Assert.Equal(AcceptResult.RejectedDedupeConflict, result);
    }

    [Fact]
    public async Task Different_activation_id_under_same_source_is_accepted()
    {
        var (store, _) = BuildStore();
        await store.TryAcceptAsync(Activation(activationId: "act-1"));

        var result = await store.TryAcceptAsync(Activation(activationId: "act-2"));

        Assert.Equal(AcceptResult.Accepted, result);
    }

    [Fact]
    public async Task Different_source_id_with_same_activation_id_is_accepted()
    {
        var (store, _) = BuildStore();
        await store.TryAcceptAsync(Activation(sourceId: "source-A"));

        var result = await store.TryAcceptAsync(Activation(sourceId: "source-B"));

        Assert.Equal(AcceptResult.Accepted, result);
    }

    [Fact]
    public async Task Mark_invalid_surfaces_dedupe_store_invalid()
    {
        var (store, _) = BuildStore();
        store.MarkInvalid();

        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.RejectedDedupeStoreInvalid, result);
    }

    [Fact]
    public async Task Recover_clears_invalid_state()
    {
        var (store, _) = BuildStore();
        store.MarkInvalid();
        store.Recover();

        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.Accepted, result);
    }

    // Plan §145: Retention drops entries older than the replay window
    // (max(MaxAge + FutureSkewTolerance + DedupeWindow, 60s)) — but
    // ALWAYS preserves the most recent entry per source so replay
    // detection across long quiet periods stays correct.
    [Fact]
    public async Task Retention_drops_stale_entries_outside_replay_window()
    {
        var (store, clock) = BuildStore();

        await store.TryAcceptAsync(Activation(activationId: "act-old"));

        // Advance past the replay window (60s floor + 12.5s headroom).
        clock.UtcNow = Now + TimeSpan.FromMinutes(2);
        await store.TryAcceptAsync(Activation(activationId: "act-fresh"));

        Assert.Equal(1, store.CountForSource("tso-source-1"));
        // The old entry was dropped; only act-fresh remains. So a
        // replay of the OLD activation id is now treated as a fresh
        // accept (the dedupe state legitimately rolled off).
        var replayOfOld = await store.TryAcceptAsync(Activation(activationId: "act-old"));
        Assert.Equal(AcceptResult.Accepted, replayOfOld);
    }

    // Plan §145 "letzter Checkpoint" guarantee: even if the single
    // remaining entry is older than the cutoff, retention preserves it
    // so a subsequent replay of THAT id is still detected.
    [Fact]
    public async Task Retention_preserves_single_last_checkpoint_even_when_stale()
    {
        var (store, clock) = BuildStore();
        await store.TryAcceptAsync(Activation(activationId: "lone-act", payloadHash: "sha256:lone"));

        clock.UtcNow = Now + TimeSpan.FromHours(1);
        // No new accept advances state; but the entry still must be
        // there. We trigger a replay to confirm dedupe state survives.
        var replay = await store.TryAcceptAsync(Activation(activationId: "lone-act", payloadHash: "sha256:lone"));

        Assert.Equal(AcceptResult.ReplayIdempotent, replay);
    }

    [Fact]
    public async Task Retention_caps_per_source_to_max_entries_per_source()
    {
        var (store, clock) = BuildStore(new RegelleistungOptions { MaxEntriesPerSource = 3 });

        // Insert 5 entries with monotonically increasing winner_chosen_at.
        for (var i = 0; i < 5; i++)
        {
            clock.UtcNow = Now + TimeSpan.FromMilliseconds(i);
            await store.TryAcceptAsync(Activation(activationId: $"act-{i}"));
        }

        Assert.Equal(3, store.CountForSource("tso-source-1"));
    }

    // Plan §145 upsert pattern pin: the SQL is INSERT ... ON CONFLICT
    // DO NOTHING with payload-hash compare on the conflict path. The
    // in-memory variant must mirror that semantic exactly so tests
    // pinning the orchestrator (Sub-Slice C) can swap the two without
    // observable behaviour changes.
    [Fact]
    public async Task Upsert_pattern_pin_replay_then_conflict()
    {
        var (store, _) = BuildStore();

        var first = await store.TryAcceptAsync(Activation(payloadHash: "sha256:aaa"));
        var sameHash = await store.TryAcceptAsync(Activation(payloadHash: "sha256:aaa"));
        var differentHash = await store.TryAcceptAsync(Activation(payloadHash: "sha256:bbb"));

        Assert.Equal(AcceptResult.Accepted, first);
        Assert.Equal(AcceptResult.ReplayIdempotent, sameHash);
        Assert.Equal(AcceptResult.RejectedDedupeConflict, differentHash);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var (store, _) = BuildStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.TryAcceptAsync(Activation(), cts.Token));
    }

    [Fact]
    public void Null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InMemoryActivationDedupeStore(null!, new FakeClock()));
    }

    [Fact]
    public void Null_clock_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InMemoryActivationDedupeStore(new RegelleistungOptions(), null!));
    }
}
