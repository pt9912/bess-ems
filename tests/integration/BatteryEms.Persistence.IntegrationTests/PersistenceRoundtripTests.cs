using BatteryEms.Adapters.Persistence;
using BatteryEms.Application.IO;
using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Npgsql;
using Xunit;

namespace BatteryEms.Persistence.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class PersistenceRoundtripTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    private NpgsqlDataSource? _dataSource;

    private static string Host => Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "127.0.0.1";
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("POSTGRES_PORT"), out var p) ? p : 5432;
    private static string Database => Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bessems";
    private static string User => Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "bessems";
    private static string Password => Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "bessems";

    public async Task InitializeAsync()
    {
        await WaitForTcpAsync(Host, Port, TimeSpan.FromSeconds(30));

        var options = PersistenceOptions.FromHostPort(Host, Port, Database, User, Password);
        _dataSource = NpgsqlDataSource.Create(options.ConnectionString);

        await new BessDbInitializer(_dataSource).InitializeAsync(CancellationToken.None);

        // Each test class run starts from a clean slate so assertions on
        // counts/last-row are stable when the compose stack is reused.
        await TruncateAllAsync(_dataSource);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
    }

    [Fact]
    public async Task Telemetry_round_trips_through_the_repository_with_DataQuality_intact()
    {
        var repo = new DapperTelemetryRepository(_dataSource!);

        var sample = new BatteryTelemetry(
            Timestamp: Now,
            AssetId: "single-bess-1",
            SocPercent: 60.5,
            SohPercent: 99,
            ActivePowerKw: -25,
            ReactivePowerKvar: 0,
            DcVoltage: 800,
            DcCurrent: -31,
            TemperatureCelsius: 22,
            Available: true,
            FaultStatus: "ok",
            DataQuality: DataQuality.Stale("aged-out"));

        await repo.AppendAsync(sample, CancellationToken.None);

        var latest = await repo.FindLatestAsync("single-bess-1", CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal(sample.SocPercent, latest!.SocPercent);
        Assert.Equal(sample.ActivePowerKw, latest.ActivePowerKw);
        Assert.Equal(DataQualityState.Stale, latest.DataQuality.Flag);
        Assert.Equal("aged-out", latest.DataQuality.Reason);

        var range = await repo.QueryAsync("single-bess-1", Now - TimeSpan.FromMinutes(5), Now + TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Single(range);
    }

    [Fact]
    public async Task Command_repository_stores_dispatch_outcome_and_supports_idempotent_append()
    {
        var repo = new DapperCommandRepository(_dataSource!);

        var command = new BatteryCommand(
            CommandId: "round-trip-1",
            Timestamp: Now,
            AssetId: "single-bess-1",
            Mode: CommandMode.Discharge,
            ActivePowerKw: 25,
            ReactivePowerKvar: 0,
            ValidUntil: Now + TimeSpan.FromSeconds(5),
            Reason: "schedule",
            Source: CommandSource.Optimization);

        var firstDispatch = CommandDispatchResult.Failed("ack-timeout", Now);
        await repo.AppendAsync(command, firstDispatch, CancellationToken.None);

        // Re-append with a later, successful dispatch — Upsert keeps the
        // latest outcome and the row count stays at 1.
        var secondDispatch = CommandDispatchResult.Ok(Now + TimeSpan.FromMilliseconds(50), "accepted");
        await repo.AppendAsync(command, secondDispatch, CancellationToken.None);

        var stored = await repo.FindByCommandIdAsync("round-trip-1", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(CommandMode.Discharge, stored!.Mode);
        Assert.Equal(25, stored.ActivePowerKw);
        Assert.Equal(CommandSource.Optimization, stored.Source);

        var latest = await repo.FindLatestAsync("single-bess-1", CancellationToken.None);
        Assert.Equal("round-trip-1", latest!.CommandId);
    }

    [Fact]
    public async Task Schedule_repository_replaces_full_window_set_atomically()
    {
        var repo = new DapperScheduleRepository(_dataSource!);

        var v1 = new Schedule("single-bess-1", ScheduleType.DayAhead, "DE-LU", 1, new List<ScheduleWindow>
        {
            new(Now, Now + TimeSpan.FromHours(1), 30),
            new(Now + TimeSpan.FromHours(1), Now + TimeSpan.FromHours(2), -20),
        });
        await repo.ReplaceAsync(v1, CancellationToken.None);

        // Replace with a v2 that has fewer windows; the previous extra
        // window must be gone, not merged.
        var v2 = new Schedule("single-bess-1", ScheduleType.DayAhead, "DE-LU", 2, new List<ScheduleWindow>
        {
            new(Now, Now + TimeSpan.FromHours(1), 15),
        });
        await repo.ReplaceAsync(v2, CancellationToken.None);

        var loaded = await repo.FindActiveAsync("single-bess-1", ScheduleType.DayAhead, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Version);
        Assert.Single(loaded.Windows);
        Assert.Equal(15, loaded.Windows[0].TargetPowerKw);
    }

    [Fact]
    public async Task Audit_log_appends_and_queries_within_window()
    {
        var log = new DapperOperatorAuditLog(_dataSource!);

        var ev = new AuditEvent(
            Timestamp: Now,
            Operator: "operator-1",
            Action: "operator-stop",
            TargetAssetId: "single-bess-1",
            Reason: "manual-shutdown",
            Outcome: "command-issued");
        await log.AppendAsync(ev, CancellationToken.None);

        var inWindow = await log.QueryAsync(Now - TimeSpan.FromMinutes(1), Now + TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Single(inWindow);
        Assert.Equal("operator-stop", inWindow[0].Action);

        // Half-open semantics: querying a window that ends exactly at Now
        // must NOT include the event whose timestamp equals Now.
        var rightOpen = await log.QueryAsync(Now - TimeSpan.FromMinutes(1), Now, CancellationToken.None);
        Assert.Empty(rightOpen);
    }

    [Fact]
    public async Task Initializer_is_idempotent_on_re_application()
    {
        // Calling InitializeAsync twice must not throw and must not break
        // existing data — IF NOT EXISTS DDL is the contract.
        var ev = new AuditEvent(Now, "operator-1", "first-run", "single-bess-1", "boot", "ok");
        await new DapperOperatorAuditLog(_dataSource!).AppendAsync(ev, CancellationToken.None);

        await new BessDbInitializer(_dataSource!).InitializeAsync(CancellationToken.None);

        var afterReinit = await new DapperOperatorAuditLog(_dataSource!).QueryAsync(
            Now - TimeSpan.FromMinutes(1),
            Now + TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.Single(afterReinit);
        Assert.Equal("first-run", afterReinit[0].Action);
    }

    private static async Task TruncateAllAsync(NpgsqlDataSource dataSource)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "TRUNCATE telemetry, commands, schedule_windows, schedules, audit_events RESTART IDENTITY CASCADE;",
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
}
