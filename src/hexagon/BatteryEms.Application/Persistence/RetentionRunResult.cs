namespace BatteryEms.Application.Persistence;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record RetentionRunResult(
    long TelemetryDeleted,
    long CommandsDeleted,
    long SchedulesDeleted,
    long OperatorAuditDeleted,
    bool OperatorAuditPreserved)
{
    // OperatorAuditPreserved=true means the policy did not configure an
    // audit retention and the use case skipped that class entirely. This
    // is the LH-PERSIST-006 "no automatic deletion of audit-relevant data
    // without explicit configuration" property surfaced at observability
    // time so logs and metrics can reflect that audit was not touched.
    public long TotalDeleted =>
        TelemetryDeleted + CommandsDeleted + SchedulesDeleted + OperatorAuditDeleted;
}
