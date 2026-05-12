using System.Threading;
using System.Threading.Tasks;
using BatteryEms.Domain;

namespace BatteryEms.Application.Mpc;

// Driven port — D-03 makes Linear-KF the default variant. Sub-Slice A
// only ships the `IdentityStateEstimator` stub (state passes through
// unchanged) so the constraint property pins can exercise the
// orchestrator without standing up the full Kalman pipeline; the
// production `DefaultLinearKalmanFilter` arrives in Sub-Slice C
// alongside the missing-measurement / unplausible-value / covariance-
// divergence reason codes from plan §3.
public interface IMpcStateEstimator
{
    Task<MpcStateUpdate> PredictUpdateAsync(
        MpcState? priorState,
        BatteryTelemetry? measurement,
        MpcModel model,
        MpcOptions options,
        CancellationToken cancellationToken);
}

// Estimator output. `IsHealthy` lets the orchestrator short-circuit a
// solver call when the estimator already saw a fail-closed condition
// (missing measurements, covariance divergence, …); the reason string
// flows straight into the `MpcDispatchResult.Reason` field so the
// validator's reason vocabulary stays the canonical surface.
public sealed record MpcStateUpdate(
    MpcState State,
    bool IsHealthy,
    string Reason);
