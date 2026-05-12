using System;

namespace BatteryEms.Application.Mpc;

// State-space constraint hull for the LTI model: hard bounds on SOC,
// active power, and per-second ramp that the solver (Sub-Slice B) and
// the validator (this slice) must honour. Plan §3 lists these three as
// the constraint axes of the initial scope; piecewise/non-linear
// elaborations are F-M5-07 follow-up work (plan §9).
//
// The validator's reason codes target this record's axes directly:
// `mpc-trajectory-soc-out-of-bounds`, `mpc-trajectory-power-out-of-bounds`,
// `mpc-trajectory-ramp-out-of-bounds`. Carry the bounds in the model so
// the Sub-Slice-D `MpcRun` stamp can hash them as part of the solver-
// config identity tuple without reaching back to BatteryAsset.
//
// SocTolerancePercent is an absolute percentage-point band around the
// SOC floor/ceiling (e.g. 1e-9 ⇒ ±1e-9 percentage points), *not* a
// fraction of the bound range. The slot was renamed from
// `SocToleranceFraction` to `*Percent` in the RM-M5-02-A review pass so
// per-asset tolerance configuration cannot mistake it for a proportional
// band.
public sealed record MpcConstraints
{
    public double MinSocPercent { get; }
    public double MaxSocPercent { get; }
    public double MinActivePowerKw { get; }
    public double MaxActivePowerKw { get; }
    public double MaxRampKwPerSecond { get; }
    public double SocTolerancePercent { get; }
    public double PowerToleranceKw { get; }
    public double RampToleranceKwPerSecond { get; }

    public MpcConstraints(
        double minSocPercent,
        double maxSocPercent,
        double minActivePowerKw,
        double maxActivePowerKw,
        double maxRampKwPerSecond,
        double socTolerancePercent = 1e-9,
        double powerToleranceKw = 1e-6,
        double rampToleranceKwPerSecond = 1e-6)
    {
        ThrowIfNotFinite(minSocPercent, nameof(minSocPercent));
        ThrowIfNotFinite(maxSocPercent, nameof(maxSocPercent));
        ThrowIfNotFinite(minActivePowerKw, nameof(minActivePowerKw));
        ThrowIfNotFinite(maxActivePowerKw, nameof(maxActivePowerKw));
        ThrowIfNotFinite(maxRampKwPerSecond, nameof(maxRampKwPerSecond));
        ThrowIfNotFinite(socTolerancePercent, nameof(socTolerancePercent));
        ThrowIfNotFinite(powerToleranceKw, nameof(powerToleranceKw));
        ThrowIfNotFinite(rampToleranceKwPerSecond, nameof(rampToleranceKwPerSecond));

        if (minSocPercent < 0 || minSocPercent >= maxSocPercent)
            throw new ArgumentOutOfRangeException(nameof(minSocPercent), "MinSocPercent must satisfy 0 <= min < max.");
        if (maxSocPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(maxSocPercent), "MaxSocPercent must be <= 100.");
        if (minActivePowerKw > maxActivePowerKw)
            throw new ArgumentOutOfRangeException(nameof(minActivePowerKw), "MinActivePowerKw must be <= MaxActivePowerKw.");
        if (maxRampKwPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRampKwPerSecond), "MaxRampKwPerSecond must be non-negative.");
        if (socTolerancePercent < 0 || powerToleranceKw < 0 || rampToleranceKwPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(socTolerancePercent), "Constraint tolerances must be non-negative.");

        MinSocPercent = minSocPercent;
        MaxSocPercent = maxSocPercent;
        MinActivePowerKw = minActivePowerKw;
        MaxActivePowerKw = maxActivePowerKw;
        MaxRampKwPerSecond = maxRampKwPerSecond;
        SocTolerancePercent = socTolerancePercent;
        PowerToleranceKw = powerToleranceKw;
        RampToleranceKwPerSecond = rampToleranceKwPerSecond;
    }

    private static void ThrowIfNotFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be finite; got {value}.");
    }
}
