using BatteryEms.Adapters.Persistence;
using BatteryEms.Application.IO;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Npgsql;
using Xunit;

namespace BatteryEms.Persistence.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class PersistenceRoundtripTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] TightSocWarnings = { "tight-binding-soc-floor" };
    private static readonly string[] SocFloorViolations = { "soc_floor_violated" };

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
    public async Task Retention_run_deletes_only_rows_older_than_cutoff_and_preserves_audit_by_default()
    {
        var telemetry = new DapperTelemetryRepository(_dataSource!);
        var commands = new DapperCommandRepository(_dataSource!);
        var audit = new DapperOperatorAuditLog(_dataSource!);
        var retention = new DapperRetentionRepository(_dataSource!);

        // Seed two telemetry samples and two audit events on either side
        // of a 30-day-old cutoff. The retention run must wipe the old
        // telemetry but leave both audit rows untouched as long as the
        // policy keeps OperatorAuditRetention=null.
        var oldTimestamp = Now - TimeSpan.FromDays(60);
        var newTimestamp = Now - TimeSpan.FromDays(10);

        await telemetry.AppendAsync(SampleTelemetry(oldTimestamp), CancellationToken.None);
        await telemetry.AppendAsync(SampleTelemetry(newTimestamp), CancellationToken.None);

        await commands.AppendAsync(
            SampleCommand("ret-old", oldTimestamp),
            CommandDispatchResult.Ok(oldTimestamp, "ok"),
            CancellationToken.None);
        await commands.AppendAsync(
            SampleCommand("ret-new", newTimestamp),
            CommandDispatchResult.Ok(newTimestamp, "ok"),
            CancellationToken.None);

        await audit.AppendAsync(
            new AuditEvent(oldTimestamp, "operator-1", "old-action", "single-bess-1", "test", "ok"),
            CancellationToken.None);
        await audit.AppendAsync(
            new AuditEvent(newTimestamp, "operator-1", "new-action", "single-bess-1", "test", "ok"),
            CancellationToken.None);

        var clock = new FixedClock(Now);
        var useCase = new RetentionRunUseCase(retention, clock);

        // Policy: retain only data within the last 30 days for telemetry +
        // commands. Audit retention left null so LH-PERSIST-006's default
        // "no auto-delete of audit-relevant data" applies.
        var policy = new RetentionPolicy(
            TelemetryRetention: TimeSpan.FromDays(30),
            CommandsRetention: TimeSpan.FromDays(30),
            SchedulesRetention: null,
            OperatorAuditRetention: null);

        var result = await useCase.ExecuteAsync(policy, CancellationToken.None);

        Assert.Equal(1, result.TelemetryDeleted);
        Assert.Equal(1, result.CommandsDeleted);
        Assert.Equal(0, result.SchedulesDeleted);
        Assert.Equal(0, result.OperatorAuditDeleted);
        Assert.True(result.OperatorAuditPreserved);

        // The newer telemetry / command is still queryable; the older one
        // is gone.
        var remainingTelemetry = await telemetry.QueryAsync(
            "single-bess-1", Now - TimeSpan.FromDays(365), Now + TimeSpan.FromDays(1), CancellationToken.None);
        Assert.Single(remainingTelemetry);
        Assert.Equal(newTimestamp, remainingTelemetry[0].Timestamp);

        Assert.Null(await commands.FindByCommandIdAsync("ret-old", CancellationToken.None));
        Assert.NotNull(await commands.FindByCommandIdAsync("ret-new", CancellationToken.None));

        // Audit was preserved because the policy kept retention null.
        var auditRows = await audit.QueryAsync(
            Now - TimeSpan.FromDays(365), Now + TimeSpan.FromDays(1), CancellationToken.None);
        Assert.Equal(2, auditRows.Count);
    }

    [Fact]
    public async Task Optimization_run_repository_round_trips_full_payload_and_supports_range_query()
    {
        var repo = new DapperOptimizationRunRepository(_dataSource!);

        var optimalRun = BuildRun(
            runId: Guid.NewGuid(),
            assetId: "single-bess-1",
            createdAt: Now,
            status: OptimizationSolverStatus.Optimal,
            terminationReason: "solver_finished",
            objectiveValue: -1234.5,
            components: new[]
            {
                new OptimizationObjectiveComponent("energy_cost", -1500.0, "EUR"),
                new OptimizationObjectiveComponent("degradation", 265.5, "EUR"),
            },
            constraintViolations: Array.Empty<string>(),
            warnings: TightSocWarnings,
            inputs: new[] { new ScheduleReference("single-bess-1", ScheduleType.DayAhead, 7) },
            producedSchedule: new ScheduleReference("single-bess-1", ScheduleType.DayAhead, 8));

        var failedRun = BuildRun(
            runId: Guid.NewGuid(),
            assetId: "single-bess-1",
            createdAt: Now + TimeSpan.FromMinutes(5),
            status: OptimizationSolverStatus.Failed,
            terminationReason: "solver_crash",
            objectiveValue: 0,
            components: Array.Empty<OptimizationObjectiveComponent>(),
            constraintViolations: SocFloorViolations,
            warnings: Array.Empty<string>(),
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: null);

        var foreignAssetRun = BuildRun(
            runId: Guid.NewGuid(),
            assetId: "single-bess-2",
            createdAt: Now + TimeSpan.FromMinutes(1),
            status: OptimizationSolverStatus.Optimal,
            terminationReason: "solver_finished",
            objectiveValue: -10,
            components: new[] { new OptimizationObjectiveComponent("energy_cost", -10, "EUR") },
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: new ScheduleReference("single-bess-2", ScheduleType.DayAhead, 1));

        await repo.AppendAsync(optimalRun, CancellationToken.None);
        await repo.AppendAsync(failedRun, CancellationToken.None);
        await repo.AppendAsync(foreignAssetRun, CancellationToken.None);

        var roundTripped = await repo.FindByIdAsync(optimalRun.RunId, CancellationToken.None);
        Assert.NotNull(roundTripped);
        Assert.Equal(optimalRun.AssetId, roundTripped!.AssetId);
        Assert.Equal(optimalRun.Status, roundTripped.Status);
        Assert.Equal(optimalRun.HorizonStart, roundTripped.HorizonStart);
        Assert.Equal(optimalRun.HorizonEnd, roundTripped.HorizonEnd);
        Assert.Equal(optimalRun.TimeStep, roundTripped.TimeStep);
        Assert.Equal(optimalRun.ObjectiveValue, roundTripped.ObjectiveValue);
        Assert.Equal(2, roundTripped.ObjectiveBreakdown.Components.Count);
        Assert.Equal("energy_cost", roundTripped.ObjectiveBreakdown.Components[0].Name);
        Assert.Equal(-1500.0, roundTripped.ObjectiveBreakdown.Components[0].Value);
        Assert.Equal("degradation", roundTripped.ObjectiveBreakdown.Components[1].Name);
        Assert.Equal(optimalRun.SolverRuntime, roundTripped.SolverRuntime);
        Assert.Equal(optimalRun.TerminationReason, roundTripped.TerminationReason);
        Assert.Equal(optimalRun.CreatedAt, roundTripped.CreatedAt);
        Assert.Empty(roundTripped.ConstraintViolations);
        Assert.Single(roundTripped.Warnings);
        Assert.Equal("tight-binding-soc-floor", roundTripped.Warnings[0]);
        Assert.Single(roundTripped.Inputs);
        Assert.Equal(7, roundTripped.Inputs[0].Version);
        Assert.NotNull(roundTripped.ProducedSchedule);
        Assert.Equal(8, roundTripped.ProducedSchedule!.Version);

        // Failed run preserves null produced schedule and one violation.
        var failedRoundTripped = await repo.FindByIdAsync(failedRun.RunId, CancellationToken.None);
        Assert.NotNull(failedRoundTripped);
        Assert.Equal(OptimizationSolverStatus.Failed, failedRoundTripped!.Status);
        Assert.Null(failedRoundTripped.ProducedSchedule);
        Assert.Single(failedRoundTripped.ConstraintViolations);
        Assert.Equal("soc_floor_violated", failedRoundTripped.ConstraintViolations[0]);
        Assert.Empty(failedRoundTripped.ObjectiveBreakdown.Components);

        // Range query: asset filter + half-open [from, until). The
        // foreign-asset run must not appear; failedRun (CreatedAt=Now+5m)
        // must be excluded by an Until=Now+5m boundary because the range
        // is right-open.
        var assetRuns = await repo.QueryAsync(
            "single-bess-1",
            Now - TimeSpan.FromMinutes(1),
            Now + TimeSpan.FromMinutes(5),
            CancellationToken.None);
        Assert.Single(assetRuns);
        Assert.Equal(optimalRun.RunId, assetRuns[0].RunId);

        // Widen the range — failedRun is now included; both ordered by CreatedAt asc.
        var bothRuns = await repo.QueryAsync(
            "single-bess-1",
            Now - TimeSpan.FromMinutes(1),
            Now + TimeSpan.FromMinutes(10),
            CancellationToken.None);
        Assert.Equal(2, bothRuns.Count);
        Assert.Equal(optimalRun.RunId, bothRuns[0].RunId);
        Assert.Equal(failedRun.RunId, bothRuns[1].RunId);
    }

    [Fact]
    public async Task Optimization_run_repository_rejects_duplicate_run_id_append()
    {
        var repo = new DapperOptimizationRunRepository(_dataSource!);
        var runId = Guid.NewGuid();

        var first = BuildRun(
            runId: runId,
            assetId: "single-bess-1",
            createdAt: Now,
            status: OptimizationSolverStatus.Optimal,
            terminationReason: "ok",
            objectiveValue: -1,
            components: new[] { new OptimizationObjectiveComponent("energy_cost", -1, "EUR") },
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: new ScheduleReference("single-bess-1", ScheduleType.DayAhead, 1));

        await repo.AppendAsync(first, CancellationToken.None);

        var second = BuildRun(
            runId: runId,
            assetId: "single-bess-1",
            createdAt: Now + TimeSpan.FromMinutes(1),
            status: OptimizationSolverStatus.Failed,
            terminationReason: "rebound",
            objectiveValue: 0,
            components: Array.Empty<OptimizationObjectiveComponent>(),
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AppendAsync(second, CancellationToken.None));
        Assert.Contains(runId.ToString(), ex.Message, StringComparison.Ordinal);
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

    private static BatteryTelemetry SampleTelemetry(DateTimeOffset timestamp) => new(
        Timestamp: timestamp,
        AssetId: "single-bess-1",
        SocPercent: 50,
        SohPercent: 99,
        ActivePowerKw: 0,
        ReactivePowerKvar: 0,
        DcVoltage: 800,
        DcCurrent: 0,
        TemperatureCelsius: 22,
        Available: true,
        FaultStatus: "ok",
        DataQuality: DataQuality.Valid);

    private static BatteryCommand SampleCommand(string id, DateTimeOffset timestamp) => new(
        CommandId: id,
        Timestamp: timestamp,
        AssetId: "single-bess-1",
        Mode: CommandMode.Idle,
        ActivePowerKw: 0,
        ReactivePowerKvar: 0,
        ValidUntil: timestamp + TimeSpan.FromMinutes(1),
        Reason: "test",
        Source: CommandSource.Optimization);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }

    private static OptimizationRun BuildRun(
        Guid runId,
        string assetId,
        DateTimeOffset createdAt,
        OptimizationSolverStatus status,
        string terminationReason,
        double objectiveValue,
        IReadOnlyList<OptimizationObjectiveComponent> components,
        IReadOnlyList<string> constraintViolations,
        IReadOnlyList<string> warnings,
        IReadOnlyList<ScheduleReference> inputs,
        ScheduleReference? producedSchedule)
    {
        var (code, detail) = OptimizationRun.ParseTerminationReason(terminationReason);
        return new OptimizationRun(
            runId: runId,
            assetId: assetId,
            solverName: "or-tools-stub",
            status: status,
            horizonStart: createdAt,
            horizonEnd: createdAt + TimeSpan.FromHours(24),
            timeStep: TimeSpan.FromMinutes(15),
            objectiveValue: objectiveValue,
            objectiveBreakdown: components.Count == 0
                ? OptimizationObjectiveBreakdown.Empty
                : new OptimizationObjectiveBreakdown(components),
            constraintViolations: constraintViolations,
            warnings: warnings,
            solverRuntime: TimeSpan.FromMilliseconds(125),
            terminationCode: code,
            terminationDetail: detail,
            createdAt: createdAt,
            inputs: inputs,
            producedSchedule: producedSchedule);
    }

    private static async Task TruncateAllAsync(NpgsqlDataSource dataSource)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var cmd = new NpgsqlCommand(
                "TRUNCATE telemetry, commands, schedule_windows, schedules, audit_events, "
                + "optimization_objective_breakdowns, optimization_runs RESTART IDENTITY CASCADE;",
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
