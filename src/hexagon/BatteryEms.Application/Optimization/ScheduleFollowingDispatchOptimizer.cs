using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// RM-M2-01: production default for IDispatchOptimizer. Selects the
// highest-priority commitment from the request's commitment list per
// LH-MKT-006 ranking and returns its PowerKw as the setpoint. With no
// dispatch-relevant commitment (empty list, or all entries Released/
// Violated) the optimiser falls back to Idle — the M1 NoOp behaviour
// callers had before.
//
// Sign convention is preserved: MarketCommitment.PowerKw and
// DispatchResult.TargetActivePowerKw both use the domain "discharge
// positive, charge negative" rule (LH §4.1), so the value passes
// through unchanged. Downstream ConstraintLimiter / RampLimiter /
// AdapterWriteLimiter still clamp the resulting setpoint against
// telemetry and asset bounds, so this optimiser does not need to
// re-validate the commitment power against the asset's operating
// envelope.
//
// LH-MKT-006 priorities #1 (Emergency Stop) and #2 (Battery/PCS
// limits) sit outside this surface — the use case short-circuits
// operator stop before calling OptimizeAsync, and the limiters apply
// after the dispatch result is returned.
public sealed class ScheduleFollowingDispatchOptimizer : IDispatchOptimizer
{
    public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestId = $"sched-{request.RequestTime.ToUnixTimeMilliseconds()}-{request.AssetId}";

        var selected = MarketCommitmentPriority.SelectHighestPriority(request.Commitments);
        if (selected is null)
        {
            return Task.FromResult(DispatchResult.Idle(requestId, "no-active-commitment"));
        }

        var rank = MarketCommitmentPriority.Rank(selected);
        var reason = $"follows-{ReasonTag(selected)}-rank-{rank}";
        return Task.FromResult(new DispatchResult(
            RequestId: requestId,
            TargetActivePowerKw: selected.PowerKw,
            Reason: reason,
            IsValid: true));
    }

    private static string ReasonTag(MarketCommitment commitment) =>
        $"{ToTag(commitment.Market)}-{ToTag(commitment.BindingState)}";

    private static string ToTag(MarketType market) => market switch
    {
        MarketType.DayAhead => "day-ahead",
        MarketType.Intraday => "intraday",
        MarketType.RegelLeistung => "regelleistung",
        _ => "unknown",
    };

    private static string ToTag(CommitmentBindingState state) => state switch
    {
        CommitmentBindingState.Pending => "pending",
        CommitmentBindingState.Binding => "binding",
        CommitmentBindingState.Released => "released",
        CommitmentBindingState.Violated => "violated",
        _ => "unknown",
    };
}
