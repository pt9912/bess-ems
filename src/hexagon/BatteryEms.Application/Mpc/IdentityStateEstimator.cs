using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BatteryEms.Domain;

namespace BatteryEms.Application.Mpc;

// Sub-Slice-A stub: passes the prior state through unchanged
// (`state_new = state_old`) and ignores measurement noise. Used by the
// constraint property pins so they exercise the wiring without standing
// up the real Kalman pipeline. Production hosts must replace this with
// `DefaultLinearKalmanFilter` (Sub-Slice C); the Sub-Slice-D production
// boot gate enforces that via `RuntimeProfile=Production` validation.
//
// When `priorState` is null (cold boot) the estimator initialises the
// state from `BatteryTelemetry.SocPercent` and uses
// `MpcEstimatorOptions.InitialCovariance` as P_0 so the orchestrator
// always sees a populated `MpcState`.
public sealed class IdentityStateEstimator : IMpcStateEstimator
{
    public Task<MpcStateUpdate> PredictUpdateAsync(
        MpcState? priorState,
        BatteryTelemetry? measurement,
        MpcModel model,
        MpcOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        if (priorState is not null)
        {
            return Task.FromResult(new MpcStateUpdate(
                priorState,
                IsHealthy: true,
                Reason: "mpc-state-passthrough"));
        }

        if (measurement is null)
        {
            return Task.FromResult(new MpcStateUpdate(
                State: BuildInitialState(model, options, socPercent: 0.0, timestamp: DateTimeOffset.MinValue),
                IsHealthy: false,
                Reason: "mpc-state-cold-boot-no-measurement"));
        }

        var initial = BuildInitialState(model, options, measurement.SocPercent, measurement.Timestamp);
        return Task.FromResult(new MpcStateUpdate(initial, IsHealthy: true, Reason: "mpc-state-cold-boot-seeded"));
    }

    private static MpcState BuildInitialState(
        MpcModel model,
        MpcOptions options,
        double socPercent,
        DateTimeOffset timestamp)
    {
        var mean = new double[model.StateDimension];
        if (mean.Length > 0)
        {
            mean[0] = socPercent;
        }
        return new MpcState(timestamp, mean, options.Estimator.InitialCovariance);
    }
}
