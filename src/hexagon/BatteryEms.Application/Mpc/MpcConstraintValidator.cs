using System;
using BatteryEms.Domain;

namespace BatteryEms.Application.Mpc;

// Reason vocabulary the validator emits. The codes are stable across
// Sub-Slices because the Sub-Slice-D persistence layer indexes
// `MpcRun.Reason` and the operator dashboard groups by it; widening the
// set is an additive change but renaming a code is a breaking one.
//
// `mpc-trajectory-ok` is the success path. The five failure codes map
// 1:1 to the Sub-Slice-A DoD property pins.
public static class MpcConstraintReasons
{
    public const string Ok = "mpc-trajectory-ok";
    public const string Empty = "mpc-trajectory-empty";
    public const string SocOutOfBounds = "mpc-trajectory-soc-out-of-bounds";
    public const string PowerOutOfBounds = "mpc-trajectory-power-out-of-bounds";
    public const string RampOutOfBounds = "mpc-trajectory-ramp-out-of-bounds";
    public const string SampleTimeMismatch = "mpc-trajectory-sample-time-mismatch";
}

public sealed record MpcConstraintCheckResult(
    bool IsValid,
    string Reason,
    int? OffendingPointIndex)
{
    public static readonly MpcConstraintCheckResult Ok =
        new(true, MpcConstraintReasons.Ok, OffendingPointIndex: null);

    public static MpcConstraintCheckResult Invalid(string reason, int? offendingPointIndex = null) =>
        new(false, reason, offendingPointIndex);
}

// Hard constraint check against the LTI model's constraint hull and the
// asset's operational limits. The validator runs the trajectory through
// three orthogonal axes (SOC bounds, signed power bounds, ramp bound)
// plus a sample-time consistency check; the empty-trajectory case is
// already caught by `MpcTrajectory`'s constructor so the validator's
// reason for that is only reachable through `Validate(null)` or via the
// pre-construction defensive path the orchestrator runs before the
// solver call.
//
// `LastObservedPowerKw` is the asset's most recent realised setpoint —
// the orchestrator passes `BatteryTelemetry.ActivePowerKw` for the
// first-segment ramp check. When the telemetry is absent (cold boot)
// the caller passes `null` and the first-segment ramp axis is skipped.
public static class MpcConstraintValidator
{
    public static MpcConstraintCheckResult Validate(
        MpcTrajectory? trajectory,
        MpcModel model,
        MpcOptions options,
        double? lastObservedPowerKw)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        if (trajectory is null || trajectory.Length == 0)
        {
            return MpcConstraintCheckResult.Invalid(MpcConstraintReasons.Empty);
        }

        if (trajectory.SampleTime != options.SampleTime)
        {
            return MpcConstraintCheckResult.Invalid(MpcConstraintReasons.SampleTimeMismatch);
        }

        var constraints = model.Constraints;
        var sampleSeconds = options.SampleTime.TotalSeconds;

        for (var i = 0; i < trajectory.Points.Count; i++)
        {
            var point = trajectory.Points[i];

            if (point.PredictedSocPercent < constraints.MinSocPercent - constraints.SocToleranceFraction
                || point.PredictedSocPercent > constraints.MaxSocPercent + constraints.SocToleranceFraction)
            {
                return MpcConstraintCheckResult.Invalid(
                    MpcConstraintReasons.SocOutOfBounds, offendingPointIndex: i);
            }

            if (point.ActivePowerKw < constraints.MinActivePowerKw - constraints.PowerToleranceKw
                || point.ActivePowerKw > constraints.MaxActivePowerKw + constraints.PowerToleranceKw)
            {
                return MpcConstraintCheckResult.Invalid(
                    MpcConstraintReasons.PowerOutOfBounds, offendingPointIndex: i);
            }

            var previousPower = i == 0
                ? lastObservedPowerKw
                : trajectory.Points[i - 1].ActivePowerKw;

            if (previousPower is not null)
            {
                var rampRate = Math.Abs(point.ActivePowerKw - previousPower.Value) / sampleSeconds;
                if (rampRate > constraints.MaxRampKwPerSecond + constraints.RampToleranceKwPerSecond)
                {
                    return MpcConstraintCheckResult.Invalid(
                        MpcConstraintReasons.RampOutOfBounds, offendingPointIndex: i);
                }
            }
        }

        return MpcConstraintCheckResult.Ok;
    }

    public static MpcConstraintCheckResult Validate(MpcTrajectory? trajectory, MpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lastPower = request.LatestMeasurement?.ActivePowerKw;
        return Validate(trajectory, request.Model, request.Options, lastPower);
    }
}
