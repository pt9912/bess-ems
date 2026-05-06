namespace BatteryEms.Domain;

// LH-PERSIST-004 + LH-OPS-004: every operator-driven action carries who,
// when, what, why and the observed outcome. Audit retention is special-
// cased in LH-PERSIST-006 (no automatic deletion without explicit
// configuration), so the storage layer treats this type as append-only.
public sealed record AuditEvent(
    DateTimeOffset Timestamp,
    string Operator,
    string Action,
    string? TargetAssetId,
    string Reason,
    string Outcome)
{
    public AuditEvent EnsureValid()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Operator);
        ArgumentException.ThrowIfNullOrWhiteSpace(Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(Outcome);
        return this;
    }
}
