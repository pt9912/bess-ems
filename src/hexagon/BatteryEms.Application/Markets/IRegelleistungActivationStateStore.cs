using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// In-memory holder for the most recent activation outcome (plan-RM-M4-
// 03 §147 audit-trail wording). The /health/regelleistung endpoint
// surfaces "last_activation" through this. Persistent audit (DB-backed
// outcome log) is a follow-up — the existing dedupe table only stores
// accepted identities, not rejected ones, and the structured ILogger
// stream covers the immediate audit need.
public interface IRegelleistungActivationStateStore
{
    void RecordOutcome(LastActivationSnapshot snapshot);
    LastActivationSnapshot? GetLast();
}

public sealed record LastActivationSnapshot(
    string SourceId,
    string ActivationId,
    DateTimeOffset ReceivedAt,
    string ReasonCode,
    bool DispatchRelevant,
    string Details);

public sealed class InMemoryRegelleistungActivationStateStore : IRegelleistungActivationStateStore
{
    private readonly object _gate = new();
    private LastActivationSnapshot? _last;

    public void RecordOutcome(LastActivationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate) { _last = snapshot; }
    }

    public LastActivationSnapshot? GetLast()
    {
        lock (_gate) { return _last; }
    }
}
