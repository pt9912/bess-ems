using BatteryEms.Application.Mpc;
using BatteryEms.Application.Persistence;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

public sealed class DapperMpcRunRepository : IMpcRunRepository
{
    private const string InsertSql = """
        INSERT INTO mpc_runs (
            mpc_request_id, asset_id, control_cycle_tick_utc, sample_time_ms,
            mpc_model_version, state_estimator_variant, solver_config_hash,
            estimator_config_hash, random_seed, numerik_stamp_json,
            p0_frobenius_display, deterministic_mode, is_usable,
            terminal_reason, trajectory_json, terminal_state_json, created_at_utc)
        VALUES (
            @MpcRequestId, @AssetId, @ControlCycleTickUtc, @SampleTimeMs,
            @MpcModelVersion, @StateEstimatorVariant, @SolverConfigHash,
            @EstimatorConfigHash, @RandomSeed, CAST(@NumerikStampJson AS jsonb),
            @P0FrobeniusDisplay, @DeterministicMode, @IsUsable,
            @TerminalReason, CAST(@TrajectoryJson AS jsonb), CAST(@TerminalStateJson AS jsonb), @CreatedAtUtc);
        """;

    private const string SelectByIdSql = """
        SELECT mpc_request_id, asset_id, control_cycle_tick_utc, sample_time_ms,
               mpc_model_version, state_estimator_variant, solver_config_hash,
               estimator_config_hash, random_seed, numerik_stamp_json::text AS numerik_stamp_json,
               p0_frobenius_display, deterministic_mode, is_usable,
               terminal_reason, trajectory_json::text AS trajectory_json,
               terminal_state_json::text AS terminal_state_json, created_at_utc
        FROM mpc_runs
        WHERE mpc_request_id = @MpcRequestId;
        """;

    private const string SelectByAssetRangeSql = """
        SELECT mpc_request_id, asset_id, control_cycle_tick_utc, sample_time_ms,
               mpc_model_version, state_estimator_variant, solver_config_hash,
               estimator_config_hash, random_seed, numerik_stamp_json::text AS numerik_stamp_json,
               p0_frobenius_display, deterministic_mode, is_usable,
               terminal_reason, trajectory_json::text AS trajectory_json,
               terminal_state_json::text AS terminal_state_json, created_at_utc
        FROM mpc_runs
        WHERE asset_id = @AssetId
          AND control_cycle_tick_utc >= @FromControlCycleTickUtc
          AND control_cycle_tick_utc < @UntilControlCycleTickUtc
        ORDER BY control_cycle_tick_utc ASC, mpc_request_id ASC;
        """;

    private const string CompactSql = """
        WITH ranked AS (
            SELECT mpc_request_id,
                   row_number() OVER (
                       PARTITION BY asset_id
                       ORDER BY created_at_utc DESC, mpc_request_id ASC) AS rn
            FROM mpc_runs
        ),
        victims AS (
            SELECT r.mpc_request_id
            FROM ranked r
            JOIN mpc_runs m ON m.mpc_request_id = r.mpc_request_id
            WHERE r.rn > @KeepLatestPerAsset
              AND m.created_at_utc < @Cutoff
        )
        DELETE FROM mpc_runs m
        USING victims v
        WHERE m.mpc_request_id = v.mpc_request_id;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public DapperMpcRunRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public async Task AppendAsync(MpcRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    InsertSql,
                    ToRow(run),
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new InvalidOperationException(
                    $"MpcRun with request id '{run.MpcRequestId}' already exists; runs are append-only.",
                    ex);
            }
        }
    }

    public async Task<MpcRun?> FindByRequestIdAsync(string mpcRequestId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpcRequestId);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
                SelectByIdSql,
                new { MpcRequestId = mpcRequestId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return row is null ? null : FromRow(row);
        }
    }

    public async Task<IReadOnlyList<MpcRun>> QueryAsync(
        string assetId,
        DateTimeOffset fromControlCycleTickUtc,
        DateTimeOffset untilControlCycleTickUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (untilControlCycleTickUtc < fromControlCycleTickUtc)
        {
            throw new ArgumentException(
                "'untilControlCycleTickUtc' must be greater than or equal to 'fromControlCycleTickUtc'.",
                nameof(untilControlCycleTickUtc));
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<RunRow>(new CommandDefinition(
                SelectByAssetRangeSql,
                new { AssetId = assetId, FromControlCycleTickUtc = fromControlCycleTickUtc, UntilControlCycleTickUtc = untilControlCycleTickUtc },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows.Select(FromRow).ToArray();
        }
    }

    public async Task<int> CompactAsync(
        MpcRunRetentionPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (policy.MaxAge is null)
        {
            return 0;
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.ExecuteAsync(new CommandDefinition(
                CompactSql,
                new
                {
                    policy.KeepLatestPerAsset,
                    Cutoff = nowUtc - policy.MaxAge.Value,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static object ToRow(MpcRun run) => new
    {
        run.MpcRequestId,
        run.AssetId,
        run.ControlCycleTickUtc,
        SampleTimeMs = (long)run.SampleTime.TotalMilliseconds,
        run.MpcModelVersion,
        run.StateEstimatorVariant,
        run.SolverConfigHash,
        run.EstimatorConfigHash,
        run.RandomSeed,
        run.NumerikStampJson,
        run.P0FrobeniusDisplay,
        DeterministicMode = run.DeterministicMode.ToString(),
        run.IsUsable,
        run.TerminalReason,
        run.TrajectoryJson,
        run.TerminalStateJson,
        run.CreatedAtUtc,
    };

    private static MpcRun FromRow(RunRow row) =>
        new(
            row.MpcRequestId,
            row.AssetId,
            row.ControlCycleTickUtc,
            TimeSpan.FromMilliseconds(row.SampleTimeMs),
            row.MpcModelVersion,
            row.StateEstimatorVariant,
            row.SolverConfigHash,
            row.EstimatorConfigHash,
            row.RandomSeed,
            row.NumerikStampJson,
            row.P0FrobeniusDisplay,
            Enum.Parse<DeterministicMode>(row.DeterministicMode, ignoreCase: false),
            row.IsUsable,
            row.TerminalReason,
            row.TrajectoryJson,
            row.TerminalStateJson,
            row.CreatedAtUtc);

    private sealed record RunRow(
        string MpcRequestId,
        string AssetId,
        DateTimeOffset ControlCycleTickUtc,
        long SampleTimeMs,
        string MpcModelVersion,
        string StateEstimatorVariant,
        string SolverConfigHash,
        string EstimatorConfigHash,
        long RandomSeed,
        string NumerikStampJson,
        double P0FrobeniusDisplay,
        string DeterministicMode,
        bool IsUsable,
        string TerminalReason,
        string? TrajectoryJson,
        string? TerminalStateJson,
        DateTimeOffset CreatedAtUtc);
}
