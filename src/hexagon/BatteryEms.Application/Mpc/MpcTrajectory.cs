using System;
using System.Collections.Generic;

namespace BatteryEms.Application.Mpc;

// One control-horizon sample. `Time` is the start-instant of the
// half-open `[Time, Time + SampleTime)` segment the setpoint applies to;
// `ActivePowerKw` follows the LH-MKT-007 sign convention shared with
// `ScheduleWindow` (positive = discharge, negative = charge — the
// validator's `MpcConstraints.{Min,Max}ActivePowerKw` bounds carry the
// signed bounds directly so the asset's split charge/discharge limits
// can be projected onto a single signed axis without re-deriving them
// at every validator call). `PredictedSocPercent` is the model's
// forward-rolled SOC for the same instant — emitted by the solver so
// the constraint validator can pin SOC-trajectory feasibility without
// re-running the state-space update itself.
public sealed record MpcTrajectoryPoint
{
    public DateTimeOffset Time { get; }
    public double ActivePowerKw { get; }
    public double PredictedSocPercent { get; }

    public MpcTrajectoryPoint(DateTimeOffset time, double activePowerKw, double predictedSocPercent)
    {
        if (!double.IsFinite(activePowerKw))
            throw new ArgumentOutOfRangeException(nameof(activePowerKw), activePowerKw, "ActivePowerKw must be finite.");
        if (!double.IsFinite(predictedSocPercent))
            throw new ArgumentOutOfRangeException(nameof(predictedSocPercent), predictedSocPercent, "PredictedSocPercent must be finite.");

        Time = time;
        ActivePowerKw = activePowerKw;
        PredictedSocPercent = predictedSocPercent;
    }
}

// Output of `IMpcModelSolver.SolveAsync` — the optimal control input
// sequence over the prediction horizon. Empty trajectories are never
// valid (plan §4 Sub-Slice-A DoD: "Empty-Trajectory-Reject" property
// pin); the constructor rejects them so the validator pin can target
// the validator's reason code rather than the constructor exception.
public sealed class MpcTrajectory
{
    public IReadOnlyList<MpcTrajectoryPoint> Points { get; }
    public TimeSpan SampleTime { get; }

    public MpcTrajectory(IReadOnlyList<MpcTrajectoryPoint> points, TimeSpan sampleTime)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (sampleTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sampleTime), sampleTime, "SampleTime must be positive.");
        if (points.Count == 0)
            throw new ArgumentException("Trajectory must contain at least one point.", nameof(points));

        var copy = new MpcTrajectoryPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(points[i]);
            if (i > 0 && points[i].Time <= points[i - 1].Time)
            {
                throw new ArgumentException(
                    $"Trajectory points must be strictly time-ordered; point {i} time {points[i].Time:O} <= point {i - 1} time {points[i - 1].Time:O}.",
                    nameof(points));
            }
            copy[i] = points[i];
        }

        Points = copy;
        SampleTime = sampleTime;
    }

    public int Length => Points.Count;
    public MpcTrajectoryPoint First => Points[0];
}
