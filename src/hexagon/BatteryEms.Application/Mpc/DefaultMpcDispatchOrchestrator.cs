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
// `BuildRequestId` packs the Sub-Slice-D identity-tuple seed: today
// only `asset_id`, `tick`, `sample_time_ms`, and `mpc_model_version`
// are folded in; Sub-Slice C adds the estimator variant and Sub-Slice D
// completes the 8-field tuple (D-09). The seed is short and human-
// readable on purpose so log lines stay grep-able; Sub-Slice D replaces
// it with the canonical SHA-256 hash of the full tuple.
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

        var requestId = BuildRequestId(request);

        var update = await _estimator.PredictUpdateAsync(
            request.PriorState,
            request.LatestMeasurement,
            request.Asset,
            request.Model,
            request.Options,
            cancellationToken).ConfigureAwait(false);

        var baseStamps = BuildBaseStamps(request, _estimator.EstimatorVariant);

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

    public static string BuildRequestId(MpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tickMs = request.CommandTick.ToUnixTimeMilliseconds();
        var sampleMs = (long)request.Options.SampleTime.TotalMilliseconds;
        return $"{request.AssetId}|{tickMs}|{sampleMs}|{request.Model.ModelVersion}";
    }

    private static Dictionary<string, string> BuildBaseStamps(MpcRequest request, string estimatorVariant) =>
        new(StringComparer.Ordinal)
        {
            ["mpc_model_version"] = request.Model.ModelVersion,
            ["estimator_variant"] = estimatorVariant,
            ["deterministic_mode"] = request.Options.DeterministicMode.ToString(),
            ["sample_time_ms"] = ((long)request.Options.SampleTime.TotalMilliseconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["horizon_length"] = request.Options.HorizonLength
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}
