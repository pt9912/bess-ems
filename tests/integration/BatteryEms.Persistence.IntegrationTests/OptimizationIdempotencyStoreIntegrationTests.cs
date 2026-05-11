using BatteryEms.Adapters.Persistence;
using BatteryEms.Application.Optimization;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BatteryEms.Persistence.IntegrationTests;

// RM-M5-01-C step 4 Persistence-Pins für den Postgres-backed
// Idempotency-Store (plan-RM-M5 §Request-Idempotenz Und Retry).
//
// Mindestens 12 Pins über die in plan §Fallback-Taxonomie genannten
// Terminalzustände + Restart-Replay + CAS-Race:
//   1. TryBegin legt einen Pending-Eintrag mit IsNewlyCreated=true an.
//   2. TryBegin zweimal mit derselben request_id liefert IsNewlyCreated=false.
//   3. TryFinalize Pending→SidecarCommitted gewinnt (true).
//   4. TryFinalize doppelt → zweiter Aufruf verliert (false, Eintrag unverändert).
//   5. TryFinalize ohne Begin → false (Eintrag existiert nicht).
//   6. Read liefert null für unbekannte request_id.
//   7. Restart-Replay: persistierter Eintrag überlebt einen Store-/
//      DataSource-Restart (frische Instanzen sehen denselben Zustand).
//   8. CAS-Race: parallele TryFinalize liefern genau einen Sieger.
//   9. FallbackCommitted-Terminal-Werte rund-trippen mit RunId/ProducedVersion.
//  10. Cancelled-Terminal-Werte rund-trippen ohne RunId/ProducedVersion.
//  11. FailedNoActivation-Terminal-Wert rund-trippt mit Reason-Text.
//  12. LateResponseIgnored ist ein erlaubter Terminal-Zustand für späte
//      Sidecar-Antworten gegen einen bereits-finalen Eintrag.
//  13. Migration 0003 ist idempotent (re-apply ist ein No-Op).
[Trait("Category", "Integration")]
[Collection("Postgres")]
public sealed class OptimizationIdempotencyStoreIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);

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

    private DapperOptimizationIdempotencyStore BuildStore()
        => new(_dataSource!);

    // Pin 1: TryBegin auf einen unbekannten request_id legt einen
    // Pending-Eintrag an und meldet IsNewlyCreated=true.
    [Fact]
    public async Task TryBegin_creates_pending_entry_when_unknown()
    {
        var store = BuildStore();

        var result = await store.TryBeginAsync(
            "req-new-1", Now, CancellationToken.None);

        Assert.True(result.IsNewlyCreated);
        Assert.Equal(OptimizationTerminalState.Pending, result.Entry.TerminalState);
        Assert.Equal("none", result.Entry.TerminalReason);
        Assert.Null(result.Entry.RunId);
        Assert.Null(result.Entry.ProducedVersion);
        Assert.Equal(Now, result.Entry.CreatedAt);
        Assert.Null(result.Entry.CommittedAt);
        Assert.False(result.Entry.IsFinal);
    }

    // Pin 2: TryBegin zweimal mit derselben request_id liefert beim
    // zweiten Aufruf IsNewlyCreated=false und den vorhandenen Eintrag.
    [Fact]
    public async Task TryBegin_returns_existing_entry_when_already_present()
    {
        var store = BuildStore();
        var first = await store.TryBeginAsync("req-dup-2", Now, CancellationToken.None);

        var second = await store.TryBeginAsync(
            "req-dup-2", Now + TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(first.IsNewlyCreated);
        Assert.False(second.IsNewlyCreated);
        // CreatedAt bleibt der ursprüngliche Wert — eine zweite
        // request_id darf nichts überschreiben.
        Assert.Equal(Now, second.Entry.CreatedAt);
        Assert.Equal(OptimizationTerminalState.Pending, second.Entry.TerminalState);
    }

    // Pin 3: TryFinalize von Pending nach SidecarCommitted gewinnt;
    // anschließend ist der Eintrag final.
    [Fact]
    public async Task TryFinalize_pending_to_sidecar_committed_wins()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-fin-3", Now, CancellationToken.None);
        var runId = Guid.NewGuid();

        var won = await store.TryFinalizeAsync(
            "req-fin-3",
            OptimizationTerminalState.SidecarCommitted,
            terminalReason: "ok",
            runId,
            producedVersion: 42,
            committedAt: Now + TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.True(won);
        var entry = await store.ReadAsync("req-fin-3", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.SidecarCommitted, entry.TerminalState);
        Assert.Equal("ok", entry.TerminalReason);
        Assert.Equal(runId, entry.RunId);
        Assert.Equal(42, entry.ProducedVersion);
        Assert.Equal(Now + TimeSpan.FromSeconds(2), entry.CommittedAt);
        Assert.True(entry.IsFinal);
    }

    // Pin 4: Doppel-TryFinalize. Der zweite Aufruf verliert (CAS,
    // terminal_state != 'Pending'); der Eintrag bleibt am Wert des
    // ersten Aufrufs.
    [Fact]
    public async Task TryFinalize_loses_when_already_final()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-fin-dup-4", Now, CancellationToken.None);
        var firstRunId = Guid.NewGuid();
        await store.TryFinalizeAsync(
            "req-fin-dup-4", OptimizationTerminalState.SidecarCommitted,
            "ok", firstRunId, producedVersion: 1,
            committedAt: Now + TimeSpan.FromSeconds(2),
            CancellationToken.None);

        var secondWon = await store.TryFinalizeAsync(
            "req-fin-dup-4", OptimizationTerminalState.FallbackCommitted,
            "late-fallback", runId: Guid.NewGuid(), producedVersion: 99,
            committedAt: Now + TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(secondWon);
        var entry = await store.ReadAsync("req-fin-dup-4", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.SidecarCommitted, entry.TerminalState);
        Assert.Equal("ok", entry.TerminalReason);
        Assert.Equal(firstRunId, entry.RunId);
        Assert.Equal(1, entry.ProducedVersion);
    }

    // Pin 5: TryFinalize gegen eine unbekannte request_id liefert
    // false (keine Zeile betroffen) und legt keinen Eintrag an.
    [Fact]
    public async Task TryFinalize_returns_false_for_unknown_request_id()
    {
        var store = BuildStore();

        var won = await store.TryFinalizeAsync(
            "req-unknown-5", OptimizationTerminalState.Cancelled,
            "no-such-request", runId: null, producedVersion: null,
            committedAt: Now, CancellationToken.None);

        Assert.False(won);
        Assert.Null(await store.ReadAsync("req-unknown-5", CancellationToken.None));
    }

    // Pin 6: Read auf unbekannte request_id liefert null.
    [Fact]
    public async Task Read_returns_null_for_unknown_request_id()
    {
        var store = BuildStore();
        var entry = await store.ReadAsync("req-no-such-6", CancellationToken.None);
        Assert.Null(entry);
    }

    // Pin 7: Restart-Replay. Eine zweite Store-Instanz (eigener
    // DataSource) liest den persistierten Eintrag der ersten Instanz
    // ohne in-memory-Zustand zu teilen.
    [Fact]
    public async Task Restart_replay_surfaces_persisted_state_in_fresh_store()
    {
        var firstStore = BuildStore();
        await firstStore.TryBeginAsync("req-restart-7", Now, CancellationToken.None);
        await firstStore.TryFinalizeAsync(
            "req-restart-7", OptimizationTerminalState.SidecarCommitted,
            "ok", Guid.NewGuid(), producedVersion: 7,
            committedAt: Now + TimeSpan.FromSeconds(1),
            CancellationToken.None);

        // Frischer DataSource ⇒ frischer Connection-Pool — simuliert
        // den Worker-Restart.
        var secondDataSource = NpgsqlDataSource.Create(_connectionString!);
        await using (secondDataSource.ConfigureAwait(false))
        {
            var secondStore = new DapperOptimizationIdempotencyStore(secondDataSource);

            var begin = await secondStore.TryBeginAsync(
                "req-restart-7", Now + TimeSpan.FromMinutes(1), CancellationToken.None);

            Assert.False(begin.IsNewlyCreated);
            Assert.Equal(OptimizationTerminalState.SidecarCommitted, begin.Entry.TerminalState);
            Assert.Equal(7, begin.Entry.ProducedVersion);
        }
    }

    // Pin 8: CAS-Race. Mehrere parallele TryFinalize-Aufrufe gegen
    // dieselbe Pending-request_id; genau einer gewinnt.
    [Fact]
    public async Task TryFinalize_concurrent_race_produces_single_winner()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-race-8", Now, CancellationToken.None);

        var tasks = new Task<bool>[8];
        for (var i = 0; i < tasks.Length; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(() => store.TryFinalizeAsync(
                "req-race-8",
                OptimizationTerminalState.SidecarCommitted,
                $"racer-{idx}",
                Guid.NewGuid(),
                producedVersion: idx,
                committedAt: Now + TimeSpan.FromMilliseconds(idx),
                CancellationToken.None));
        }
        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(1, outcomes.Count(b => b));
        var entry = await store.ReadAsync("req-race-8", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.SidecarCommitted, entry.TerminalState);
    }

    // Pin 9: FallbackCommitted mit RunId/ProducedVersion rundtripped.
    [Fact]
    public async Task TryFinalize_fallback_committed_roundtrips_run_and_version()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-fb-9", Now, CancellationToken.None);
        var runId = Guid.NewGuid();

        var won = await store.TryFinalizeAsync(
            "req-fb-9", OptimizationTerminalState.FallbackCommitted,
            "sidecar-deadline-exceeded", runId, producedVersion: 12,
            committedAt: Now + TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.True(won);
        var entry = await store.ReadAsync("req-fb-9", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.FallbackCommitted, entry.TerminalState);
        Assert.Equal("sidecar-deadline-exceeded", entry.TerminalReason);
        Assert.Equal(runId, entry.RunId);
        Assert.Equal(12, entry.ProducedVersion);
    }

    // Pin 10: Cancelled-Terminal ohne RunId/ProducedVersion (Operator-
    // Cancel vor Sidecar-Antwort) rundtripped als NULL.
    [Fact]
    public async Task TryFinalize_cancelled_roundtrips_null_run_and_version()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-cancel-10", Now, CancellationToken.None);

        var won = await store.TryFinalizeAsync(
            "req-cancel-10", OptimizationTerminalState.Cancelled,
            "operator-cancel", runId: null, producedVersion: null,
            committedAt: Now + TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(won);
        var entry = await store.ReadAsync("req-cancel-10", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.Cancelled, entry.TerminalState);
        Assert.Null(entry.RunId);
        Assert.Null(entry.ProducedVersion);
    }

    // Pin 11: FailedNoActivation-Terminal rundtrip mit Reason-Text.
    [Fact]
    public async Task TryFinalize_failed_no_activation_roundtrips_reason()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-fail-11", Now, CancellationToken.None);

        var won = await store.TryFinalizeAsync(
            "req-fail-11", OptimizationTerminalState.FailedNoActivation,
            "sidecar-and-fallback-both-failed", runId: null, producedVersion: null,
            committedAt: Now + TimeSpan.FromSeconds(4),
            CancellationToken.None);

        Assert.True(won);
        var entry = await store.ReadAsync("req-fail-11", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.FailedNoActivation, entry.TerminalState);
        Assert.Equal("sidecar-and-fallback-both-failed", entry.TerminalReason);
    }

    // Pin 12: LateResponseIgnored. Späte Sidecar-Antwort gegen eine
    // bereits-finale request_id — der Worker setzt diesen Terminal-
    // Wert nicht als Folgezustand (CAS verliert), sondern verwendet ihn
    // als initialen Terminal-Wert für eine NEUE request_id wenn die
    // Sidecar-Antwort nach einer Cancellation eintrudelt und kein
    // begleitender Begin-Eintrag mehr existiert. Hier pinnen wir die
    // Persistenz-Eigenschaft: der Wert ist DDL-/Enum-mäßig zulässig
    // und überlebt einen Roundtrip.
    [Fact]
    public async Task TryFinalize_late_response_ignored_roundtrips()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-late-12", Now, CancellationToken.None);

        var won = await store.TryFinalizeAsync(
            "req-late-12", OptimizationTerminalState.LateResponseIgnored,
            "post-cancellation-sidecar-result", runId: null, producedVersion: null,
            committedAt: Now + TimeSpan.FromSeconds(6),
            CancellationToken.None);

        Assert.True(won);
        var entry = await store.ReadAsync("req-late-12", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.LateResponseIgnored, entry.TerminalState);
    }

    // Pin 13: Migration 0003 ist idempotent. Re-apply darf weder
    // CREATE-Errors werfen noch Daten überschreiben — der bereits
    // angelegte Eintrag muss erhalten bleiben.
    [Fact]
    public async Task Migration_0003_is_idempotent_on_second_call()
    {
        var store = BuildStore();
        await store.TryBeginAsync("req-mig-13", Now, CancellationToken.None);

        await new BessDbMigrator(
            _dataSource!, _connectionString!, NullLogger<BessDbMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);

        var entry = await store.ReadAsync("req-mig-13", CancellationToken.None);
        Assert.NotNull(entry);
        Assert.Equal(OptimizationTerminalState.Pending, entry.TerminalState);
    }

    private static async Task TruncateAsync(NpgsqlDataSource dataSource)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "TRUNCATE optimization_idempotency RESTART IDENTITY CASCADE;",
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
                if (probe.Connected) { return; }
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
}
