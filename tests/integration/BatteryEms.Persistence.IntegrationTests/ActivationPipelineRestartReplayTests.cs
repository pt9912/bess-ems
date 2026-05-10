using BatteryEms.Adapters.Persistence;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BatteryEms.Persistence.IntegrationTests;

// RM-M4-03-E persistence pins (plan §148):
//   - Restart-Replay: a previously accepted activation, after the
//     in-memory state is dropped, surfaces as ReplayIdempotent through
//     the full use-case pipeline because the persistent dedupe table
//     still holds the entry.
//   - Conflicting replay across "restart": same identity + different
//     payload → DedupeConflict.
//   - Persistenz-Determinismus: an explicit tiebreak scenario lands
//     deterministic state in the dedupe table — both candidates are
//     persisted, replays of either surface as ReplayIdempotent.
[Trait("Category", "Integration")]
[Collection("Postgres")]
public sealed class ActivationPipelineRestartReplayTests : IAsyncLifetime
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

    // Builds a fresh use-case pipeline against the same DB-backed
    // dedupe store. Each call simulates a process restart by creating
    // new in-memory state holders (timebase, dispatch source, state
    // store) while the Dapper dedupe store is reconstructed against
    // the persistent table.
    private (DefaultRegelleistungActivationUseCase UseCase,
             InMemoryActivationDispatchSource DispatchSource,
             DapperActivationDedupeStore Dedupe) BuildFreshPipeline()
    {
        var options = new RegelleistungOptions
        {
            ProductionActivationEnabled = true,
            ProductTrustEstablished = true,
        };
        var clock = new FixedClock(Now);
        var dedupe = new DapperActivationDedupeStore(
            _dataSource!, options, clock,
            NullLogger<DapperActivationDedupeStore>.Instance);
        var validator = new ActivationValidator(options, dedupe, clock);
        var dispatchSource = new InMemoryActivationDispatchSource();
        var useCase = new DefaultRegelleistungActivationUseCase(
            validator,
            new InMemoryTimebaseHealthSource(),
            dispatchSource,
            new HealthyProductionPreconditionProvider(),
            new InMemoryRegelleistungActivationStateStore(),
            options, clock,
            NullLogger<DefaultRegelleistungActivationUseCase>.Instance);
        return (useCase, dispatchSource, dedupe);
    }

    private static RegelleistungActivation Activation(
        string sourceId = "tso-source-1",
        string activationId = "act-1",
        long sequenceNumber = 1,
        string payloadHash = "sha256:abc",
        DateTimeOffset? signalTimestamp = null)
        => new(
            sourceId, activationId, sequenceNumber,
            signalTimestamp ?? Now,
            ReserveProduct.Afrr, ReserveDirection.Up,
            powerKw: 25,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash);

    [Fact]
    public async Task Restart_replay_through_use_case_surfaces_replay_idempotent()
    {
        var (firstUseCase, firstDispatch, _) = BuildFreshPipeline();
        var firstOutcome = await firstUseCase.ReceiveAsync(Activation(payloadHash: "sha256:p"));
        Assert.True(firstOutcome.DispatchRelevant);
        Assert.NotNull(firstDispatch.GetActive(Now));

        // Simulate restart — fresh in-memory state holders, same DB.
        var (secondUseCase, secondDispatch, _) = BuildFreshPipeline();
        Assert.Null(secondDispatch.GetActive(Now));

        var replay = await secondUseCase.ReceiveAsync(Activation(payloadHash: "sha256:p"));

        Assert.False(replay.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, replay.ReasonCode);
        // Pin: dispatch source stays empty after restart — replay is
        // not re-fed to dispatch (matches use-case contract from D).
        Assert.Null(secondDispatch.GetActive(Now));
    }

    [Fact]
    public async Task Conflicting_replay_after_restart_surfaces_dedupe_conflict()
    {
        var (firstUseCase, _, _) = BuildFreshPipeline();
        await firstUseCase.ReceiveAsync(Activation(payloadHash: "sha256:original"));

        var (secondUseCase, _, _) = BuildFreshPipeline();
        var conflict = await secondUseCase.ReceiveAsync(Activation(payloadHash: "sha256:tampered"));

        Assert.False(conflict.DispatchRelevant);
        Assert.Equal(ActivationValidationReasons.DedupeConflict, conflict.ReasonCode);
    }

    // Plan §148 Persistenz-Determinismus pin: a tiebreak scenario
    // produces deterministic dedupe-table state — both candidates
    // are persisted with their identity tuples, and replays of either
    // surface ReplayIdempotent at reload.
    [Fact]
    public async Task Tiebreak_scenario_persists_deterministically_and_replays_idempotently()
    {
        var (useCase, dispatch, _) = BuildFreshPipeline();

        var b = Activation(sourceId: "source-zeta", activationId: "act-z",
            sequenceNumber: 5, payloadHash: "sha256:zeta");
        var a = Activation(sourceId: "source-alpha", activationId: "act-a",
            sequenceNumber: 5, payloadHash: "sha256:alpha");

        await useCase.ReceiveAsync(b);
        await useCase.ReceiveAsync(a);

        // Tiebreak winner: lex-smaller (source_id, activation_id) — "source-alpha".
        Assert.Equal("source-alpha", dispatch.GetActive(Now)!.SourceId);
        // Both rows in the persistent dedupe table.
        Assert.Equal(2, await CountRowsAsync());

        // Restart and replay both — both are recognised as
        // ReplayIdempotent regardless of order.
        var (restartedUseCase, _, _) = BuildFreshPipeline();
        var replayB = await restartedUseCase.ReceiveAsync(b);
        var replayA = await restartedUseCase.ReceiveAsync(a);

        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, replayB.ReasonCode);
        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, replayA.ReasonCode);
        // Pin: persistent state did not double-write.
        Assert.Equal(2, await CountRowsAsync());
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

    private static async Task TruncateAsync(NpgsqlDataSource dataSource)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "TRUNCATE regelleistung_activations RESTART IDENTITY CASCADE;", connection);
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
                if (probe.Connected) return;
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
