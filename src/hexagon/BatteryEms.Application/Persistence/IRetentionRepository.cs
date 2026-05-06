namespace BatteryEms.Application.Persistence;

// LH-PERSIST-006 cleanup port. Each method deletes rows older than the
// passed cutoff and returns the affected row count for observability.
// Cutoff semantics are exclusive: rows with timestamp < cutoff go away,
// rows whose timestamp equals cutoff stay. The use case computes the
// cutoff as `now - retention` so the data within retention is preserved.
//
// Schedules are deleted when *all* of their windows end before cutoff —
// i.e. the schedule's horizon is fully in the past. The CASCADE FK on
// schedule_windows takes care of dependent rows.
public interface IRetentionRepository
{
    Task<long> DeleteTelemetryOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);

    Task<long> DeleteCommandsOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);

    Task<long> DeleteSchedulesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);

    Task<long> DeleteOperatorAuditOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
