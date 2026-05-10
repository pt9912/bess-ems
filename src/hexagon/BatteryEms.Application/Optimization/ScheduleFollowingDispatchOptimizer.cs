using BatteryEms.Application.Markets;
using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// RM-M2-01: production default for IDispatchOptimizer. Selects the
// highest-priority commitment from the request's commitment list per
// LH-MKT-006 ranking and returns its PowerKw as the setpoint. With no
// dispatch-relevant commitment (empty list, or all entries Released/
// Violated) the optimiser falls back to Idle — the M1 NoOp behaviour
// callers had before.
//
// RM-M4-03-D (D-09 choice c) extension: ScheduleFollowingDispatchOptimizer
// also consults IActivationDispatchSource per tick. A non-null active
// activation wins over every MarketCommitment (rank 3 ahead of ranks
// 4-6 from LH-MKT-006). The activation source returns null for
// expired entries, so request.RequestTime acts as the tick clock —
// no IClock dependency is added.
//
// Sign convention is preserved: MarketCommitment.PowerKw,
// RegelleistungActivation.PowerKw, and DispatchResult.TargetActivePowerKw
// all use the domain "discharge positive, charge negative" rule
// (LH §4.1), so the value passes through unchanged from either the
// activation or the selected commitment. Downstream ConstraintLimiter
// / RampLimiter / AdapterWriteLimiter still clamp the resulting
// setpoint against telemetry and asset bounds, so this optimiser does
// not need to re-validate the activation/commitment power against the
// asset's operating envelope.
//
// LH-MKT-006 priorities #1 (Emergency Stop) and #2 (Battery/PCS
// limits) sit outside this surface — the use case short-circuits
// operator stop before calling OptimizeAsync, and the limiters apply
// after the dispatch result is returned.
public sealed class ScheduleFollowingDispatchOptimizer : IDispatchOptimizer
{
    private readonly IActivationDispatchSource _activationSource;

    public ScheduleFollowingDispatchOptimizer(IActivationDispatchSource activationSource)
    {
        ArgumentNullException.ThrowIfNull(activationSource);
        _activationSource = activationSource;
    }

    public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestId = $"sched-{request.RequestTime.ToUnixTimeMilliseconds()}-{request.AssetId}";

        // Activation always wins (rank 3) when present and within
        // validity for this tick. The source's CoversValidity check is
        // half-open against request.RequestTime, so an activation
        // expiring at exactly RequestTime is already discarded. Sub-
        // Slice D ships single-asset; activation→asset routing is a
        // follow-up when multi-asset deployments land — for now any
        // active activation overrides the schedule on every tick.
        var activation = _activationSource.GetActive(request.RequestTime);
        if (activation is not null)
        {
            return Task.FromResult(new DispatchResult(
                RequestId: requestId,
                TargetActivePowerKw: ActivationPowerKwSigned(activation),
                Reason: $"follows-regelleistung-activation-rank-{MarketCommitmentPriority.RegelLeistung}",
                IsValid: true));
        }

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

    private static double ActivationPowerKwSigned(RegelleistungActivation activation)
    {
        // PowerKw on RegelleistungActivation is a magnitude (>= 0);
        // ReserveDirection encodes the sign convention. Up = upward
        // regulation = discharge = positive; Down = downward regulation
        // = charge = negative; Symmetric (FCR) keeps the magnitude
        // positive — the use-case decides FCR semantics elsewhere.
        return activation.Direction switch
        {
            ReserveDirection.Up => activation.PowerKw,
            ReserveDirection.Down => -activation.PowerKw,
            ReserveDirection.Symmetric => activation.PowerKw,
            _ => activation.PowerKw,
        };
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
