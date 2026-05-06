namespace BatteryEms.Domain;

// LH-PERSIST-006 retention configuration: per-data-class TimeSpan with
// null = retain forever. The audit class deliberately defaults to null so
// "no automatic deletion of audit-relevant data without explicit
// configuration" is a property of the default-constructed policy, not a
// convention an operator might forget.
//
// The Application-side use case turns each non-null retention into a
// cutoff = now - retention and asks the persistence adapter to delete
// rows older than that. A null entry skips the corresponding class
// entirely, which is the safe path on a misconfigured system.
public sealed record RetentionPolicy(
    TimeSpan? TelemetryRetention,
    TimeSpan? CommandsRetention,
    TimeSpan? SchedulesRetention,
    TimeSpan? OperatorAuditRetention)
{
    // Conservative default: nothing gets auto-deleted. Operators opt in
    // per data class; audit stays null even after opting in unless they
    // also set OperatorAuditRetention explicitly (LH-PERSIST-006).
    public static RetentionPolicy AuditPreserved { get; } = new(
        TelemetryRetention: null,
        CommandsRetention: null,
        SchedulesRetention: null,
        OperatorAuditRetention: null);

    public RetentionPolicy EnsureValid()
    {
        Reject(TelemetryRetention, nameof(TelemetryRetention));
        Reject(CommandsRetention, nameof(CommandsRetention));
        Reject(SchedulesRetention, nameof(SchedulesRetention));
        Reject(OperatorAuditRetention, nameof(OperatorAuditRetention));
        return this;
    }

    private static void Reject(TimeSpan? value, string field)
    {
        if (value is { } v && v < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                field,
                v,
                "Retention duration must be non-negative; null disables auto-delete for the data class.");
        }
    }
}
