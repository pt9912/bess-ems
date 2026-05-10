namespace BatteryEms.Domain;

// Operator-tunable surface for Regelleistung activation handling
// (plan-RM-M4-03 §144 + §147). Defaults are the master-DoD values
// pinned verbatim — the Defaults pin test in Domain.Tests guards
// against silent drift on MaxAge/FutureSkewTolerance/DedupeWindow.
//
// MaxEntriesPerSource is intentionally NOT pinned: the master DoD
// wording is "eine konfigurierte Obergrenze" without a specific number,
// so the operator-tunable default lives here and can move without a
// pin-test change. Sub-Slice B's persistent dedupe tracker honours it
// for retention compaction.
//
// ProductionActivationEnabled (default false) is the Sub-Slice D
// master switch: while false, every accepted activation surfaces as
// not-dispatch-relevant (audit-only). When true, the use-case still
// requires the four production-gate pre-conditions to be green;
// any failed pre-condition keeps the outcome not-dispatch-relevant.
//
// ProductTrustEstablished (default false) is the boolean operator-
// trust stamp from D-03 — it cannot default to true; an operator
// has to set it explicitly before production activation flows can
// dispatch.
public sealed record RegelleistungOptions
{
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan FutureSkewTolerance { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan DedupeWindow { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxEntriesPerSource { get; init; } = 10_000;

    public bool ProductionActivationEnabled { get; init; }
    public bool ProductTrustEstablished { get; init; }

    public RegelleistungOptions EnsureValid()
    {
        if (MaxAge <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"MaxAge must be positive (got {MaxAge}).",
                nameof(MaxAge));
        }
        if (FutureSkewTolerance < TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"FutureSkewTolerance must be non-negative (got {FutureSkewTolerance}).",
                nameof(FutureSkewTolerance));
        }
        if (DedupeWindow <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"DedupeWindow must be positive (got {DedupeWindow}).",
                nameof(DedupeWindow));
        }
        if (MaxEntriesPerSource <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxEntriesPerSource),
                MaxEntriesPerSource,
                "MaxEntriesPerSource must be positive.");
        }
        return this;
    }
}
