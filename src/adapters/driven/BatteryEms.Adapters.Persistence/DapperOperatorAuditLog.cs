using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

public sealed class DapperOperatorAuditLog : IOperatorAuditLog
{
    private const string InsertSql = """
        INSERT INTO audit_events (
            recorded_at, operator_id, action, target_asset_id, reason, outcome)
        VALUES (
            @RecordedAt, @OperatorId, @Action, @TargetAssetId, @Reason, @Outcome);
        """;

    private const string SelectRangeSql = """
        SELECT recorded_at, operator_id, action, target_asset_id, reason, outcome
        FROM audit_events
        WHERE recorded_at >= @From AND recorded_at < @Until
        ORDER BY recorded_at ASC;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public DapperOperatorAuditLog(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        auditEvent.EnsureValid();

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertSql,
                new
                {
                    RecordedAt = auditEvent.Timestamp,
                    OperatorId = auditEvent.Operator,
                    Action = auditEvent.Action,
                    TargetAssetId = auditEvent.TargetAssetId,
                    Reason = auditEvent.Reason,
                    Outcome = auditEvent.Outcome,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<AuditRow>(new CommandDefinition(
                SelectRangeSql,
                new { From = from, Until = until },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows
                .Select(r => new AuditEvent(
                    TimestampConverter.ToOffset(r.RecordedAt),
                    r.OperatorId,
                    r.Action,
                    r.TargetAssetId,
                    r.Reason,
                    r.Outcome))
                .ToArray();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Dapper via reflection.")]
    private sealed class AuditRow
    {
        public DateTime RecordedAt { get; init; }
        public string OperatorId { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string? TargetAssetId { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string Outcome { get; init; } = string.Empty;
    }
}
