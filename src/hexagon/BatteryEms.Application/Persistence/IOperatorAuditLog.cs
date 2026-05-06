using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// LH-PERSIST-004 / LH-OPS-004 — audit log is append-only by contract:
// LH-PERSIST-006 forbids automatic deletion of audit-relevant data
// without explicit configuration, so this port intentionally exposes
// no Delete or Truncate. Retention rules live in RM-M1-14.
public interface IOperatorAuditLog
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    // Half-open range [from, until) — same convention as ITelemetryRepository.
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken);
}
