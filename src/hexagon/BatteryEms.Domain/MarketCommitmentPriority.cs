namespace BatteryEms.Domain;

// LH-MKT-006 priority ranking for MarketCommitment-driven dispatch.
// Lower number = higher priority (matches the spec list 1..7). The
// ranking is a pure function — same inputs produce the same output,
// no clock/RNG dependency, framework-free — analog to RampLimiter and
// ConstraintLimiter elsewhere in Domain.
//
// Three priority slots from the spec are NOT modelled here because
// they sit OUTSIDE the IDispatchOptimizer surface:
//   #1 Emergency Stop      — short-circuited in ControlCycleUseCase
//                            before the optimiser is consulted.
//   #2 Battery/PCS limits  — applied AFTER the optimiser by
//                            ConstraintLimiter / RampLimiter /
//                            AdapterWriteLimiter.
//   #7 Lokale Optimierung  — the fallback when no commitment is
//                            present; ScheduleFollowingDispatchOptimizer
//                            returns Idle in that case.
//
// The remaining priorities #3..#6 are what this primitive ranks:
//   #3 RegelLeistung       — any RegelLeistung commitment present in
//                            the schedule means the battery holds
//                            reserved capacity at this priority.
//                            Real frequency-response activation is
//                            M4 (RM-M4-03); M2-01 uses the static
//                            PowerKw as the setpoint.
//   #4 Verbindliche Markt- — Binding commitments of energy-market
//      verpflichtungen       type (DayAhead/Intraday with
//                            BindingState.Binding). M2 maps DayAhead
//                            schedules to Binding by default per
//                            DefaultScheduleTracker / LH-MKT-001.
//   #5 Intraday-Fahrplan   — Intraday commitments without a binding
//                            contract.
//   #6 Day-Ahead-Fahrplan  — DayAhead commitments without a binding
//                            contract.
//
// Released and Violated commitments do not drive dispatch; the
// SelectHighestPriority helper filters them out.
public static class MarketCommitmentPriority
{
    public const int RegelLeistung = 3;
    public const int BindingMarkt = 4;
    public const int IntradaySchedule = 5;
    public const int DayAheadSchedule = 6;
    public const int NotApplicable = int.MaxValue;

    public static int Rank(MarketCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);

        if (commitment.BindingState is CommitmentBindingState.Released
            or CommitmentBindingState.Violated)
        {
            return NotApplicable;
        }

        return commitment.Market switch
        {
            MarketType.RegelLeistung => RegelLeistung,
            _ when commitment.BindingState == CommitmentBindingState.Binding => BindingMarkt,
            MarketType.Intraday => IntradaySchedule,
            MarketType.DayAhead => DayAheadSchedule,
            _ => NotApplicable,
        };
    }

    // Returns the highest-priority commitment from the list (lowest
    // Rank value), or null if no commitment is dispatch-relevant
    // (empty list, or all entries are Released/Violated). Stable on
    // ties: returns the first commitment in input order with the
    // minimum rank, so a deterministic upstream order produces a
    // deterministic dispatch.
    public static MarketCommitment? SelectHighestPriority(
        IReadOnlyList<MarketCommitment> commitments)
    {
        ArgumentNullException.ThrowIfNull(commitments);

        MarketCommitment? best = null;
        var bestRank = NotApplicable;
        foreach (var commitment in commitments)
        {
            ArgumentNullException.ThrowIfNull(commitment);
            var rank = Rank(commitment);
            if (rank < bestRank)
            {
                best = commitment;
                bestRank = rank;
            }
        }
        return best;
    }
}
