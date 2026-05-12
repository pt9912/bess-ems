using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryEms.Application.Mpc;

// Wireing class. Per-tick the orchestrator runs three steps in fixed
// order:
//   1. State estimation via `IMpcStateEstimator.PredictUpdateAsync`.
//      Fail-closed if the estimator reports unhealthy state — the
//      result carries the estimator's reason code unchanged so callers
//      do not need to translate.
//   2. Trajectory solve via `IMpcModelSolver.SolveAsync` against the
//      posterior state. Sub-Slice A registers the
//      `NotImplementedMpcModelSolver` stub which throws — the
//      orchestrator does not swallow that exception; it lets the
//      Sub-Slice-B integration pin observe the real wire path.
//   3. Constraint validation via `MpcConstraintValidator.Validate`.
//      Invalid trajectories produce a not-usable result; the reason
//      code is the validator's, not the orchestrator's, so the reason
//      vocabulary stays single-sourced.
//
// `MpcRunIdentity` packs the Sub-Slice-D identity tuple and stamps. The
// orchestrator builds it once before estimator/solver work so unhealthy
// estimator exits and successful trajectories carry the same replay key.
public sealed class DefaultMpcDispatchOrchestrator : IMpcDispatchOptimizer
{
    private readonly IMpcStateEstimator _estimator;
    private readonly IMpcModelSolver _solver;

    public DefaultMpcDispatchOrchestrator(IMpcStateEstimator estimator, IMpcModelSolver solver)
    {
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentNullException.ThrowIfNull(solver);
        _estimator = estimator;
        _solver = solver;
    }

    public async Task<MpcDispatchResult> NextStepAsync(
        MpcRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identity = MpcRunIdentity.Build(request, _estimator.EstimatorVariant);
        var requestId = identity.MpcRequestId;

        var update = await _estimator.PredictUpdateAsync(
            request.PriorState,
            request.LatestMeasurement,
            request.Asset,
            request.Model,
            request.Options,
            cancellationToken).ConfigureAwait(false);

        var baseStamps = identity.ToStamps();

        if (!update.IsHealthy)
        {
            return MpcDispatchResult.NotUsable(
                requestId,
                update.Reason,
                baseStamps,
                posteriorState: update.State);
        }

        var trajectory = await _solver.SolveAsync(
            update.State,
            request.Model,
            request.Options,
            request.CommandTick,
            cancellationToken).ConfigureAwait(false);

        var check = MpcConstraintValidator.Validate(trajectory, request);
        if (!check.IsValid)
        {
            return MpcDispatchResult.NotUsable(
                requestId,
                check.Reason,
                baseStamps,
                posteriorState: update.State);
        }

        return MpcDispatchResult.Usable(
            requestId,
            trajectory,
            update.State,
            baseStamps);
    }

    public static string BuildRequestId(MpcRequest request, string stateEstimatorVariant)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateEstimatorVariant);
        return MpcRunIdentity.Build(request, stateEstimatorVariant).MpcRequestId;
    }
}
