using System;
using BatteryEms.Application.Mpc;
using Xunit;

namespace BatteryEms.Application.Tests.Mpc;

// Sub-Slice-A constraint-property pins. The DoD calls for 5+ pins
// across SOC-bounds, Power-bounds, Ramp-bounds, Reason-pinning, and
// Empty-rejection; this file delivers 11 fact pins + 6 theory rows to
// give the validator's reason vocabulary deterministic coverage. Each
// pin targets exactly one constraint axis so a regression points
// straight at the broken bound.
public sealed class MpcConstraintValidatorTests
{
    [Fact]
    public void Valid_trajectory_passes()
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions();
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (20.0, 50.0), (20.0, 49.0), (20.0, 48.0) });

        var result = MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: 20.0);

        Assert.True(result.IsValid);
        Assert.Equal(MpcConstraintReasons.Ok, result.Reason);
        Assert.Null(result.OffendingPointIndex);
    }

    // Pin 1 — SOC-in-Bounds: predicted SOC below the floor fails.
    [Fact]
    public void Soc_below_minimum_fails_with_soc_reason()
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions();
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (0.0, 50.0), (0.0, 9.5), (0.0, 8.0) });

        var result = MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: 0.0);

        Assert.False(result.IsValid);
        Assert.Equal(MpcConstraintReasons.SocOutOfBounds, result.Reason);
        Assert.Equal(1, result.OffendingPointIndex);
    }

    // Pin 1b — SOC above the ceiling fails on the same axis.
    [Fact]
    public void Soc_above_maximum_fails_with_soc_reason()
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions();
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (0.0, 89.0), (0.0, 90.5) });

        var result = MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: 0.0);

        Assert.False(result.IsValid);
        Assert.Equal(MpcConstraintReasons.SocOutOfBounds, result.Reason);
        Assert.Equal(1, result.OffendingPointIndex);
    }

    // Pin 2 — Power-in-Bounds: setpoint outside [min, max] fails on
    // the power axis. We give SOC inside-bounds so the SOC axis can't
    // mask the failure.
    [Theory]
    [InlineData(-51.0)]
    [InlineData(60.0)]
    public void Power_out_of_bounds_fails_with_power_reason(double offendingPowerKw)
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions();
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (0.0, 50.0), (offendingPowerKw, 50.0) });

        // Allow the ramp delta from 0 → offending to keep the power axis isolated.
        var generousModel = new MpcModel(
            model.ModelVersion,
            model.A, model.B, model.C, model.D,
            new MpcConstraints(
                minSocPercent: 10, maxSocPercent: 90,
                minActivePowerKw: -50, maxActivePowerKw: 50,
                maxRampKwPerSecond: 10_000));

        var result = MpcConstraintValidator.Validate(trajectory, generousModel, options, lastObservedPowerKw: 0.0);

        Assert.False(result.IsValid);
        Assert.Equal(MpcConstraintReasons.PowerOutOfBounds, result.Reason);
        Assert.Equal(1, result.OffendingPointIndex);
    }

    // Pin 3 — Ramp-in-Bounds across consecutive trajectory points.
    [Fact]
    public void Ramp_between_consecutive_points_fails_with_ramp_reason()
    {
        var model = MpcTestFixtures.BuildModel(MpcTestFixtures.BuildConstraints(maxRampKwPerSecond: 10));
        var options = MpcTestFixtures.BuildOptions(sampleTime: TimeSpan.FromSeconds(1));
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (0.0, 50.0), (30.0, 50.0) }); // 30 kW/s > 10 kW/s

        var result = MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: 0.0);

        Assert.False(result.IsValid);
        Assert.Equal(MpcConstraintReasons.RampOutOfBounds, result.Reason);
        Assert.Equal(1, result.OffendingPointIndex);
    }

    // Pin 3b — Ramp axis is also checked against `lastObservedPowerKw`
    // for the very first segment so a discontinuity between sensed
    // power and the new setpoint surfaces immediately.
    [Fact]
    public void Ramp_from_last_observation_fails_with_ramp_reason()
    {
        var model = MpcTestFixtures.BuildModel(MpcTestFixtures.BuildConstraints(maxRampKwPerSecond: 10));
        var options = MpcTestFixtures.BuildOptions(sampleTime: TimeSpan.FromSeconds(1));
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (50.0, 50.0) }); // jump from 0 → 50 in 1s

        var result = MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: 0.0);

        Assert.False(result.IsValid);
        Assert.Equal(MpcConstraintReasons.RampOutOfBounds, result.Reason);
        Assert.Equal(0, result.OffendingPointIndex);
    }

    // Pin 3c — Ramp axis is skipped on segment 0 when there is no
    // `lastObservedPowerKw`. Cold-boot path must not fail on the
    // first segment alone.
    [Fact]
    public void Ramp_skipped_on_first_segment_when_no_prior_observation()
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions(sampleTime: TimeSpan.FromSeconds(1));
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (50.0, 50.0) });

        var result = MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: null);

        Assert.True(result.IsValid);
        Assert.Equal(MpcConstraintReasons.Ok, result.Reason);
    }

    // Pin 4 — Constraint-Violation-Reason pinning. The five public
    // reason codes are stable strings the persistence and dashboard
    // layers index by; this pin asserts the canonical spellings so
    // a rename surfaces here before it ripples downstream.
    [Theory]
    [InlineData(nameof(MpcConstraintReasons.Ok), "mpc-trajectory-ok")]
    [InlineData(nameof(MpcConstraintReasons.Empty), "mpc-trajectory-empty")]
    [InlineData(nameof(MpcConstraintReasons.SocOutOfBounds), "mpc-trajectory-soc-out-of-bounds")]
    [InlineData(nameof(MpcConstraintReasons.PowerOutOfBounds), "mpc-trajectory-power-out-of-bounds")]
    [InlineData(nameof(MpcConstraintReasons.RampOutOfBounds), "mpc-trajectory-ramp-out-of-bounds")]
    [InlineData(nameof(MpcConstraintReasons.SampleTimeMismatch), "mpc-trajectory-sample-time-mismatch")]
    public void Reason_codes_have_stable_spelling(string memberName, string expected)
    {
        var actual = typeof(MpcConstraintReasons)
            .GetField(memberName)!
            .GetRawConstantValue() as string;
        Assert.Equal(expected, actual);
    }

    // Pin 5 — Empty-Trajectory-Reject. The `MpcTrajectory` constructor
    // already rejects an empty point list (so the validator's `Empty`
    // path is only reachable via a null trajectory reference). Pin
    // both halves of that split so future refactors keep the contract.
    [Fact]
    public void Null_trajectory_fails_with_empty_reason()
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions();

        var result = MpcConstraintValidator.Validate(trajectory: null, model, options, lastObservedPowerKw: null);

        Assert.False(result.IsValid);
        Assert.Equal(MpcConstraintReasons.Empty, result.Reason);
        Assert.Null(result.OffendingPointIndex);
    }

    [Fact]
    public void Empty_trajectory_constructor_rejects_zero_points()
    {
        Assert.Throws<ArgumentException>(() => new MpcTrajectory(Array.Empty<MpcTrajectoryPoint>(), TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void Sample_time_mismatch_fails_with_sample_time_reason()
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions(sampleTime: TimeSpan.FromMilliseconds(250));
        var trajectoryWithWrongStep = MpcTestFixtures.BuildTrajectory(
            sampleTime: TimeSpan.FromMilliseconds(500),
            segments: new[] { (0.0, 50.0) });

        var result = MpcConstraintValidator.Validate(trajectoryWithWrongStep, model, options, lastObservedPowerKw: 0.0);

        Assert.False(result.IsValid);
        Assert.Equal(MpcConstraintReasons.SampleTimeMismatch, result.Reason);
    }

    // Tolerance pin — within-tolerance violations on each axis do not
    // trip the validator. This guarantees the solver-reported
    // floating-point dust does not cascade into a false reject when
    // the solution is feasible up to the configured tolerance.
    // The `lastObservedPower` is held equal to the segment power so
    // the ramp axis cannot mask the SOC/power tolerance assertions.
    [Theory]
    [InlineData(9.9999999995, 0.0)]   // SOC 5e-10 below floor — within 1e-9
    [InlineData(50.0, 50.0000005)]    // power 5e-7 above ceiling — within 1e-6
    public void Within_tolerance_violations_still_pass(double soc, double power)
    {
        var model = MpcTestFixtures.BuildModel();
        var options = MpcTestFixtures.BuildOptions();
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: options.SampleTime,
            segments: new[] { (power, soc) });

        var result = MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: power);

        Assert.True(result.IsValid, $"Expected valid, got reason {result.Reason} at point {result.OffendingPointIndex}.");
    }
}
