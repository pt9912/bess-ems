using BatteryEms.Application.Persistence;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

public sealed class DapperRetentionRepository : IRetentionRepository
{
    // Telemetry / commands / audit deletes are straight column-cutoff
    // SQL. Schedules need NOT EXISTS over schedule_windows because a
    // schedule with even one in-future window is still relevant to the
    // tracker; CASCADE on the FK takes care of the windows once the
    // header row is gone.
    private const string DeleteTelemetrySql =
        "DELETE FROM telemetry WHERE recorded_at < @Cutoff;";

    private const string DeleteCommandsSql =
        "DELETE FROM commands WHERE issued_at < @Cutoff;";

    private const string DeleteSchedulesSql = """
        DELETE FROM schedules
        WHERE NOT EXISTS (
            SELECT 1
            FROM schedule_windows w
            WHERE w.asset_id = schedules.asset_id
              AND w.type = schedules.type
              AND w.window_end >= @Cutoff
        );
        """;

    private const string DeleteOperatorAuditSql =
        "DELETE FROM audit_events WHERE recorded_at < @Cutoff;";

    private readonly NpgsqlDataSource _dataSource;

    public DapperRetentionRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public Task<long> DeleteTelemetryOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        ExecuteCutoffAsync(DeleteTelemetrySql, cutoff, cancellationToken);

    public Task<long> DeleteCommandsOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        ExecuteCutoffAsync(DeleteCommandsSql, cutoff, cancellationToken);

    public Task<long> DeleteSchedulesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        ExecuteCutoffAsync(DeleteSchedulesSql, cutoff, cancellationToken);

    public Task<long> DeleteOperatorAuditOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        ExecuteCutoffAsync(DeleteOperatorAuditSql, cutoff, cancellationToken);

    private async Task<long> ExecuteCutoffAsync(string sql, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { Cutoff = cutoff },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }
}
