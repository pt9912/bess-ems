using BatteryEms.Adapters.Persistence;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BatteryEms.Persistence.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("Postgres")]
public sealed class ActivationDedupeStoreIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private NpgsqlDataSource? _dataSource;
    private string? _connectionString;

    private static string Host => Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "127.0.0.1";
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("POSTGRES_PORT"), out var p) ? p : 5432;
    private static string Database => Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bessems";
    private static string User => Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "bessems";
    private static string Password => Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "bessems";

    public async Task InitializeAsync()
    {
        await WaitForTcpAsync(Host, Port, TimeSpan.FromSeconds(30));

        var options = PersistenceOptions.FromHostPort(Host, Port, Database, User, Password);
        _connectionString = options.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(_connectionString);

        await new BessDbMigrator(
            _dataSource, _connectionString, NullLogger<BessDbMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);

        await TruncateAsync(_dataSource);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
    }

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

    private DapperActivationDedupeStore BuildStore(
        RegelleistungOptions? options = null,
        DateTimeOffset? clockNow = null)
        => new(
            _dataSource!,
            options ?? new RegelleistungOptions(),
            new FixedClock(clockNow ?? Now),
            NullLogger<DapperActivationDedupeStore>.Instance);

    [Fact]
    public async Task First_accept_persists_and_returns_accepted()
    {
        var store = BuildStore();

        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.Accepted, result);
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    public async Task Replay_with_same_payload_returns_idempotent_and_does_not_double_insert()
    {
        var store = BuildStore();
        await store.TryAcceptAsync(Activation(payloadHash: "sha256:abc"));

        var result = await store.TryAcceptAsync(Activation(payloadHash: "sha256:abc"));

        Assert.Equal(AcceptResult.ReplayIdempotent, result);
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    public async Task Same_identity_with_different_payload_returns_dedupe_conflict()
    {
        var store = BuildStore();
        await store.TryAcceptAsync(Activation(payloadHash: "sha256:original"));

        var result = await store.TryAcceptAsync(Activation(payloadHash: "sha256:tampered"));

        Assert.Equal(AcceptResult.RejectedDedupeConflict, result);
        // The conflicting attempt rolled back; the original payload is still stored.
        var storedHash = await SelectPayloadHashAsync("tso-source-1", "act-1");
        Assert.Equal("sha256:original", storedHash);
    }

    [Fact]
    public async Task Restart_replay_loads_persistent_state_and_detects_idempotent_replay()
    {
        var firstStore = BuildStore();
        await firstStore.TryAcceptAsync(Activation(payloadHash: "sha256:persistent"));

        // Simulate a process restart by constructing a fresh store
        // instance against the same database. The persisted row must
        // surface the replay as idempotent without any in-memory state
        // being carried over.
        var secondStore = BuildStore();
        var result = await secondStore.TryAcceptAsync(Activation(payloadHash: "sha256:persistent"));

        Assert.Equal(AcceptResult.ReplayIdempotent, result);
    }

    [Fact]
    public async Task Migration_0002_is_idempotent_on_second_call()
    {
        await new BessDbMigrator(
            _dataSource!, _connectionString!, NullLogger<BessDbMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);

        var store = BuildStore();
        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.Accepted, result);
    }

    // Plan §145 sub-case (a): incompatible checkpoint — DbUp journal
    // reports a migration newer than the running build's known set.
    [Fact]
    public async Task Tracker_load_fail_closed_on_incompatible_checkpoint()
    {
        // Insert a synthetic future migration entry into __schema_versions.
        var connection = await _dataSource!.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO __schema_versions (scriptname, applied) VALUES (@n, NOW());",
                connection);
            cmd.Parameters.AddWithValue("n", "BatteryEms.Adapters.Persistence.Migrations.RunOnce.9999_future.sql");
            await cmd.ExecuteNonQueryAsync();
        }

        var store = BuildStore();
        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.RejectedDedupeStoreInvalid, result);
    }

    // Plan §145 sub-case (b): oversize checkpoint — per-source row
    // count exceeds RegelleistungOptions.MaxEntriesPerSource.
    [Fact]
    public async Task Tracker_load_fail_closed_on_oversize_per_source()
    {
        // Insert 5 rows for a single source, then construct a store
        // whose options cap that source at 3.
        for (var i = 0; i < 5; i++)
        {
            await InsertRowAsync(
                sourceId: "noisy-source",
                activationId: $"act-{i}",
                payloadHash: $"sha256:{i}",
                winnerChosenAt: Now + TimeSpan.FromSeconds(i));
        }

        var store = BuildStore(options: new RegelleistungOptions { MaxEntriesPerSource = 3 });
        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.RejectedDedupeStoreInvalid, result);
    }

    // Plan §145 sub-case (c): partial corruption — a row with empty
    // payload_hash. DDL allows the empty string (NOT NULL is the only
    // guard); the app-level check rejects it.
    [Fact]
    public async Task Tracker_load_fail_closed_on_empty_payload_hash()
    {
        await InsertRowAsync(
            sourceId: "corrupt-source",
            activationId: "act-corrupt",
            payloadHash: string.Empty,
            winnerChosenAt: Now);

        var store = BuildStore();
        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.RejectedDedupeStoreInvalid, result);
    }

    // Plan §145 sub-case (c): partial corruption — negative sequence_number.
    [Fact]
    public async Task Tracker_load_fail_closed_on_negative_sequence_number()
    {
        await InsertRowAsync(
            sourceId: "corrupt-source",
            activationId: "act-corrupt",
            payloadHash: "sha256:ok",
            sequenceNumber: -1,
            winnerChosenAt: Now);

        var store = BuildStore();
        var result = await store.TryAcceptAsync(Activation());

        Assert.Equal(AcceptResult.RejectedDedupeStoreInvalid, result);
    }

    // Plan §145 sub-case (d): generic parse/decode fail — covered by
    // the catch-all in DapperActivationDedupeStore. Simulate by
    // disposing the data source out from under the store.
    [Fact]
    public async Task Tracker_load_fail_closed_on_unexpected_error()
    {
        // Build a store against a data source that points at a host
        // that won't accept connections — the validation queries throw
        // and the catch-all marks the store invalid.
        var deadDataSource = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=x;Timeout=1;Command Timeout=1;");
        await using (deadDataSource.ConfigureAwait(false))
        {
            var store = new DapperActivationDedupeStore(
                deadDataSource,
                new RegelleistungOptions(),
                new FixedClock(Now),
                NullLogger<DapperActivationDedupeStore>.Instance);

            var result = await store.TryAcceptAsync(Activation());

            Assert.Equal(AcceptResult.RejectedDedupeStoreInvalid, result);
        }
    }

    [Fact]
    public async Task Reset_for_recovery_clears_invalid_after_underlying_fix()
    {
        await InsertRowAsync(
            sourceId: "corrupt-source",
            activationId: "act-corrupt",
            payloadHash: string.Empty,
            winnerChosenAt: Now);

        var store = BuildStore();
        var firstResult = await store.TryAcceptAsync(Activation());
        Assert.Equal(AcceptResult.RejectedDedupeStoreInvalid, firstResult);

        // Operator clears the corrupt row, then resets the store.
        await DeleteRowAsync("corrupt-source", "act-corrupt");
        store.ResetForRecovery();

        var afterReset = await store.TryAcceptAsync(Activation());
        Assert.Equal(AcceptResult.Accepted, afterReset);
    }

    // Plan §145 retention pin: entries older than the replay window
    // (max(MaxAge + FutureSkewTolerance + DedupeWindow, 60s) measured
    // by winner_chosen_at) get dropped on the next accept — but the
    // single most recent entry per source is always preserved.
    [Fact]
    public async Task Retention_drops_stale_entries_outside_replay_window()
    {
        var options = new RegelleistungOptions();

        var oldStore = new DapperActivationDedupeStore(
            _dataSource!, options, new FixedClock(Now),
            NullLogger<DapperActivationDedupeStore>.Instance);
        await oldStore.TryAcceptAsync(Activation(activationId: "act-old"));

        // Advance past the replay-window floor (60s) by a comfortable margin.
        var freshStore = new DapperActivationDedupeStore(
            _dataSource!, options, new FixedClock(Now + TimeSpan.FromMinutes(2)),
            NullLogger<DapperActivationDedupeStore>.Instance);
        await freshStore.TryAcceptAsync(Activation(activationId: "act-fresh"));

        Assert.Equal(1, await CountRowsForSourceAsync("tso-source-1"));
        // The old row was compacted; replaying the OLD activation id is
        // now treated as a fresh accept (dedupe state legitimately
        // rolled off).
        var replayOfOld = await freshStore.TryAcceptAsync(Activation(activationId: "act-old"));
        Assert.Equal(AcceptResult.Accepted, replayOfOld);
    }

    // Plan §145 "letzter Checkpoint" guarantee: even when retention
    // could in principle drop everything (long quiet period), the
    // single newest entry per source survives so a subsequent replay
    // of THAT id is still detected.
    [Fact]
    public async Task Retention_preserves_single_last_checkpoint_even_when_stale()
    {
        var firstStore = new DapperActivationDedupeStore(
            _dataSource!, new RegelleistungOptions(), new FixedClock(Now),
            NullLogger<DapperActivationDedupeStore>.Instance);
        await firstStore.TryAcceptAsync(Activation(activationId: "lone-act", payloadHash: "sha256:lone"));

        // Construct a fresh store far in the future. Without any new
        // accept the prior compaction can't drop the lone entry; a
        // replay must still surface as idempotent.
        var laterStore = new DapperActivationDedupeStore(
            _dataSource!, new RegelleistungOptions(), new FixedClock(Now + TimeSpan.FromHours(1)),
            NullLogger<DapperActivationDedupeStore>.Instance);
        var replay = await laterStore.TryAcceptAsync(
            Activation(activationId: "lone-act", payloadHash: "sha256:lone"));

        Assert.Equal(AcceptResult.ReplayIdempotent, replay);
    }

    // Plan §145 retention compaction pin: per-source cap is enforced
    // on each accept; the most recent entry survives, older ones get
    // dropped to keep total at MaxEntriesPerSource.
    [Fact]
    public async Task Retention_caps_per_source_to_max_entries()
    {
        var options = new RegelleistungOptions { MaxEntriesPerSource = 3 };
        var t = Now;
        var clock = new FixedClock(t);

        // Insert 5 entries with monotonically increasing winner_chosen_at
        // by constructing fresh stores so each accept uses a stepped
        // clock.
        for (var i = 0; i < 5; i++)
        {
            var stepped = new DapperActivationDedupeStore(
                _dataSource!,
                options,
                new FixedClock(t + TimeSpan.FromSeconds(i)),
                NullLogger<DapperActivationDedupeStore>.Instance);
            var result = await stepped.TryAcceptAsync(Activation(activationId: $"act-{i}"));
            Assert.Equal(AcceptResult.Accepted, result);
        }

        Assert.Equal(3, await CountRowsForSourceAsync("tso-source-1"));
    }

    private async Task<long> CountRowsAsync()
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM regelleistung_activations;", connection);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
    }

    private async Task<long> CountRowsForSourceAsync(string sourceId)
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM regelleistung_activations WHERE source_id = @s;", connection);
            cmd.Parameters.AddWithValue("s", sourceId);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
    }

    private async Task<string?> SelectPayloadHashAsync(string sourceId, string activationId)
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT payload_hash FROM regelleistung_activations WHERE source_id = @s AND activation_id = @a;",
                connection);
            cmd.Parameters.AddWithValue("s", sourceId);
            cmd.Parameters.AddWithValue("a", activationId);
            return (string?)await cmd.ExecuteScalarAsync();
        }
    }

    private async Task InsertRowAsync(
        string sourceId,
        string activationId,
        string payloadHash,
        DateTimeOffset winnerChosenAt,
        long sequenceNumber = 1)
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO regelleistung_activations
                  (source_id, activation_id, sequence_number, signal_timestamp_utc,
                   payload_hash, winner_chosen_at)
                VALUES (@s, @a, @n, @t, @h, @w);
                """, connection);
            cmd.Parameters.AddWithValue("s", sourceId);
            cmd.Parameters.AddWithValue("a", activationId);
            cmd.Parameters.AddWithValue("n", sequenceNumber);
            cmd.Parameters.AddWithValue("t", Now);
            cmd.Parameters.AddWithValue("h", payloadHash);
            cmd.Parameters.AddWithValue("w", winnerChosenAt);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task DeleteRowAsync(string sourceId, string activationId)
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM regelleistung_activations WHERE source_id = @s AND activation_id = @a;",
                connection);
            cmd.Parameters.AddWithValue("s", sourceId);
            cmd.Parameters.AddWithValue("a", activationId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task TruncateAsync(NpgsqlDataSource dataSource)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "TRUNCATE regelleistung_activations RESTART IDENTITY CASCADE; "
                + "DELETE FROM __schema_versions WHERE scriptname LIKE '%9999_future%';",
                connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task WaitForTcpAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await probe.ConnectAsync(host, port, probeCts.Token);
                if (probe.Connected)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
            {
                lastError = ex;
            }
            await Task.Delay(200);
        }
        throw new InvalidOperationException(
            $"Postgres at {host}:{port} did not accept TCP connections within {timeout}: {lastError?.Message}");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }
}
