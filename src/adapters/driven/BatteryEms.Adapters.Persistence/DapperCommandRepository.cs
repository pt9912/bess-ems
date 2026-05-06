using BatteryEms.Application.IO;
using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

public sealed class DapperCommandRepository : ICommandRepository
{
    // ON CONFLICT DO UPDATE because the control loop replays the last
    // command on retries (RM-M1-07 fallback path) and the dispatch
    // adapter may produce a fresh CommandDispatchResult for the same
    // CommandId on the second attempt. Storing the latest dispatch
    // outcome is what the audit trail wants; the original intent stays
    // preserved through CommandId immutability.
    private const string UpsertSql = """
        INSERT INTO commands (
            command_id, asset_id, issued_at, mode,
            active_power_kw, reactive_power_kvar, valid_until,
            reason, source,
            dispatch_success, dispatch_reason, dispatched_at)
        VALUES (
            @CommandId, @AssetId, @IssuedAt, @Mode,
            @ActivePowerKw, @ReactivePowerKvar, @ValidUntil,
            @Reason, @Source,
            @DispatchSuccess, @DispatchReason, @DispatchedAt)
        ON CONFLICT (command_id) DO UPDATE SET
            dispatch_success = EXCLUDED.dispatch_success,
            dispatch_reason = EXCLUDED.dispatch_reason,
            dispatched_at = EXCLUDED.dispatched_at;
        """;

    private const string SelectByIdSql = """
        SELECT command_id, asset_id, issued_at, mode,
               active_power_kw, reactive_power_kvar, valid_until,
               reason, source
        FROM commands
        WHERE command_id = @CommandId;
        """;

    private const string SelectLatestSql = """
        SELECT command_id, asset_id, issued_at, mode,
               active_power_kw, reactive_power_kvar, valid_until,
               reason, source
        FROM commands
        WHERE asset_id = @AssetId
        ORDER BY issued_at DESC
        LIMIT 1;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public DapperCommandRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public async Task AppendAsync(BatteryCommand command, CommandDispatchResult dispatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dispatch);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                UpsertSql,
                new
                {
                    CommandId = command.CommandId,
                    AssetId = command.AssetId,
                    IssuedAt = command.Timestamp,
                    Mode = command.Mode.ToString(),
                    ActivePowerKw = command.ActivePowerKw,
                    ReactivePowerKvar = command.ReactivePowerKvar,
                    ValidUntil = command.ValidUntil,
                    Reason = command.Reason,
                    Source = command.Source.ToString(),
                    DispatchSuccess = dispatch.Success,
                    DispatchReason = dispatch.Reason,
                    DispatchedAt = dispatch.DispatchedAt,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task<BatteryCommand?> FindByCommandIdAsync(string commandId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var row = await connection.QuerySingleOrDefaultAsync<CommandRow>(new CommandDefinition(
                SelectByIdSql,
                new { CommandId = commandId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return row is null ? null : FromRow(row);
        }
    }

    public async Task<BatteryCommand?> FindLatestAsync(string assetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var row = await connection.QuerySingleOrDefaultAsync<CommandRow>(new CommandDefinition(
                SelectLatestSql,
                new { AssetId = assetId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return row is null ? null : FromRow(row);
        }
    }

    private static BatteryCommand FromRow(CommandRow r) => new(
        CommandId: r.CommandId,
        Timestamp: TimestampConverter.ToOffset(r.IssuedAt),
        AssetId: r.AssetId,
        Mode: Enum.Parse<CommandMode>(r.Mode),
        ActivePowerKw: r.ActivePowerKw,
        ReactivePowerKvar: r.ReactivePowerKvar,
        ValidUntil: TimestampConverter.ToOffset(r.ValidUntil),
        Reason: r.Reason,
        Source: Enum.Parse<CommandSource>(r.Source));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Dapper via reflection.")]
    private sealed class CommandRow
    {
        public string CommandId { get; init; } = string.Empty;
        public string AssetId { get; init; } = string.Empty;
        public DateTime IssuedAt { get; init; }
        public string Mode { get; init; } = string.Empty;
        public double ActivePowerKw { get; init; }
        public double? ReactivePowerKvar { get; init; }
        public DateTime ValidUntil { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
    }
}
