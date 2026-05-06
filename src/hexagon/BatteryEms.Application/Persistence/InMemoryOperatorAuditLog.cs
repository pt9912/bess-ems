using System.Collections.Concurrent;
using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// Process-local drop-in for IOperatorAuditLog. The API host (RM-M1-16)
// records operator-stop attempts here so it stays free of the Dapper /
// Postgres adapter; production swaps in DapperOperatorAuditLog at the
// Composition Root in RM-M1-19. Append-only by contract (LH-PERSIST-006
// forbids automatic deletion of audit-relevant data).
public sealed class InMemoryOperatorAuditLog : IOperatorAuditLog
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        auditEvent.EnsureValid();
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        if (until < from)
        {
            throw new ArgumentException("'until' must be greater than or equal to 'from'.", nameof(until));
        }
        IReadOnlyList<AuditEvent> result = _events
            .Where(e => e.Timestamp >= from && e.Timestamp < until)
            .OrderBy(e => e.Timestamp)
            .ToArray();
        return Task.FromResult(result);
    }
}
