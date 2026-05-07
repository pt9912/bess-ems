using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class PidControllerTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1);

    private static PidControllerOptions Options(
        double kp = 0,
        double ki = 0,
        double kd = 0,
        double outputMin = double.NegativeInfinity,
        double outputMax = double.PositiveInfinity,
        double deadband = 0,
        PidAntiWindupMode antiWindup = PidAntiWindupMode.ConditionalIntegration) =>
        new()
        {
            Kp = kp,
            Ki = ki,
            Kd = kd,
            OutputMin = outputMin,
            OutputMax = outputMax,
            DeadbandAbsolute = deadband,
            AntiWindupMode = antiWindup,
        };

    // --- Proportional ------------------------------------------------------

    [Fact]
    public void P_only_output_equals_kp_times_error()
    {
        var result = PidController.Step(
            PidControllerState.Initial,
            Options(kp: 2.0),
            setpoint: 10,
            measurement: 4,
            Dt);

        Assert.Equal(12.0, result.Output);
        Assert.False(result.WasClamped);
        Assert.False(result.WasIntegralFrozen);
        Assert.Equal(0.0, result.NextState.Integral);
        Assert.Equal(6.0, result.NextState.PreviousError);
    }

    [Fact]
    public void P_only_with_zero_error_is_zero()
    {
        var result = PidController.Step(
            PidControllerState.Initial,
            Options(kp: 5.0),
            setpoint: 7,
            measurement: 7,
            Dt);

        Assert.Equal(0.0, result.Output);
    }

    // --- Integral ----------------------------------------------------------

    [Fact]
    public void I_only_accumulates_per_step_at_constant_error()
    {
        var options = Options(ki: 0.5);
        var state = PidControllerState.Initial;

        for (var i = 1; i <= 4; i++)
        {
            var result = PidController.Step(state, options, setpoint: 10, measurement: 8, Dt);
            Assert.Equal(0.5 * 2.0 * i, result.Output, precision: 12);
            state = result.NextState;
        }
    }

    [Fact]
    public void I_only_with_dt_half_accumulates_half_per_step()
    {
        var options = Options(ki: 1.0);
        var dt = TimeSpan.FromSeconds(0.5);
        var state = PidControllerState.Initial;

        var first = PidController.Step(state, options, setpoint: 10, measurement: 6, dt);
        Assert.Equal(2.0, first.Output, precision: 12);

        var second = PidController.Step(first.NextState, options, setpoint: 10, measurement: 6, dt);
        Assert.Equal(4.0, second.Output, precision: 12);
    }

    // --- Derivative --------------------------------------------------------

    [Fact]
    public void D_only_reacts_to_error_change()
    {
        var options = Options(kd: 3.0);
        var initial = PidControllerState.Initial with { PreviousError = 5 };

        var result = PidController.Step(initial, options, setpoint: 10, measurement: 8, Dt);

        // de = 2 - 5 = -3, D = 3.0 * -3 / 1 = -9
        Assert.Equal(-9.0, result.Output, precision: 12);
    }

    [Fact]
    public void D_only_at_constant_error_is_zero_after_warm_start()
    {
        var options = Options(kd: 10.0);
        var warm = PidControllerState.Initial with { PreviousError = 4 };

        var result = PidController.Step(warm, options, setpoint: 10, measurement: 6, Dt);

        Assert.Equal(0.0, result.Output, precision: 12);
    }

    // --- Output clamping ---------------------------------------------------

    [Fact]
    public void Output_is_clamped_to_max()
    {
        var result = PidController.Step(
            PidControllerState.Initial,
            Options(kp: 100, outputMax: 10),
            setpoint: 5,
            measurement: 0,
            Dt);

        Assert.Equal(10.0, result.Output);
        Assert.True(result.WasClamped);
    }

    [Fact]
    public void Output_is_clamped_to_min()
    {
        var result = PidController.Step(
            PidControllerState.Initial,
            Options(kp: 100, outputMin: -5),
            setpoint: 0,
            measurement: 5,
            Dt);

        Assert.Equal(-5.0, result.Output);
        Assert.True(result.WasClamped);
    }

    [Fact]
    public void Output_within_bounds_is_not_marked_clamped()
    {
        var result = PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1, outputMin: -10, outputMax: 10),
            setpoint: 5,
            measurement: 0,
            Dt);

        Assert.Equal(5.0, result.Output);
        Assert.False(result.WasClamped);
    }

    // --- Anti-windup -------------------------------------------------------

    [Fact]
    public void Conditional_integration_plateaus_integral_at_saturation_boundary()
    {
        // Kp=Kd=0 so output equals the integral; Ki small enough that
        // the integrator walks up gradually instead of overshooting the
        // bound on step 1. Without anti-windup the integral after 100
        // steps would be 100 * 0.1 * 10 * 1 = 100. Conditional
        // integration plateaus it once a further update would push the
        // candidate past OutputMax while the error still pulls upward.
        var options = Options(ki: 0.1, outputMax: 5);
        var state = PidControllerState.Initial;

        for (var i = 0; i < 100; i++)
        {
            state = PidController.Step(state, options, setpoint: 10, measurement: 0, Dt).NextState;
        }

        Assert.Equal(5.0, state.Integral, precision: 12);
    }

    [Fact]
    public void Conditional_integration_unwinds_when_error_reverses()
    {
        var options = Options(ki: 0.1, outputMax: 5);
        var state = PidControllerState.Initial;

        // Saturate against +5 (integral plateaus at OutputMax).
        for (var i = 0; i < 100; i++)
        {
            state = PidController.Step(state, options, setpoint: 10, measurement: 0, Dt).NextState;
        }
        Assert.Equal(5.0, state.Integral, precision: 12);

        // Reverse the error: setpoint < measurement → integral must
        // decrement immediately, not wait for the saturation to clear.
        var step = PidController.Step(state, options, setpoint: 0, measurement: 5, Dt);

        Assert.True(step.NextState.Integral < 5.0,
            $"Integral failed to unwind: stayed at {step.NextState.Integral}.");
        Assert.False(step.WasIntegralFrozen);
    }

    [Fact]
    public void Conditional_integration_does_not_freeze_when_inside_bounds()
    {
        var options = Options(ki: 1.0, outputMax: 100);
        var first = PidController.Step(
            PidControllerState.Initial,
            options,
            setpoint: 10, measurement: 0,
            Dt);

        Assert.False(first.WasIntegralFrozen);
        Assert.Equal(10.0, first.NextState.Integral, precision: 12);
    }

    [Fact]
    public void Saturation_at_lower_bound_with_positive_error_does_not_freeze()
    {
        // Pre-load a negative integral so the candidate output starts
        // below OutputMin even though the current error is positive
        // (would pull the output up). Anti-windup must NOT freeze here
        // because the integration would relieve, not worsen, the
        // saturation.
        var options = Options(ki: 1.0, outputMin: -5);
        var state = PidControllerState.Initial with { Integral = -20 };

        var result = PidController.Step(state, options, setpoint: 10, measurement: 0, Dt);

        Assert.False(result.WasIntegralFrozen);
        Assert.Equal(-10.0, result.NextState.Integral, precision: 12);
    }

    // --- Deadband ----------------------------------------------------------

    [Fact]
    public void Deadband_suppresses_output_for_small_errors()
    {
        var options = Options(kp: 5, ki: 1, kd: 1, deadband: 1.5);
        var warm = PidControllerState.Initial with { PreviousError = 0 };

        var result = PidController.Step(warm, options, setpoint: 10, measurement: 9.5, Dt);

        Assert.Equal(0.0, result.Output);
        Assert.Equal(0.0, result.NextState.Integral);
        Assert.Equal(0.0, result.NextState.PreviousError);
    }

    [Fact]
    public void Deadband_holds_integrated_output_when_error_re_enters_band()
    {
        var options = Options(kp: 0, ki: 1, kd: 0, deadband: 0.5);
        var state = PidControllerState.Initial;

        // Drive error large to accumulate integral.
        state = PidController.Step(state, options, setpoint: 10, measurement: 0, Dt).NextState;
        var heldIntegral = state.Integral;
        Assert.Equal(10.0, heldIntegral, precision: 12);

        // Now error is well inside the deadband — output should hold at
        // the previously accumulated integral, not collapse.
        var result = PidController.Step(state, options, setpoint: 10, measurement: 9.9, Dt);

        Assert.Equal(heldIntegral, result.NextState.Integral, precision: 12);
        Assert.Equal(heldIntegral, result.Output, precision: 12);
    }

    [Fact]
    public void Deadband_at_exact_threshold_is_outside_band()
    {
        // Half-open convention: |error| < deadband enters; |error| ==
        // deadband stays active. Ensures small errors that exactly
        // match the threshold still drive the integrator.
        var options = Options(ki: 1, deadband: 2);

        var result = PidController.Step(
            PidControllerState.Initial,
            options,
            setpoint: 10, measurement: 8,
            Dt);

        Assert.Equal(2.0, result.NextState.Integral, precision: 12);
    }

    [Fact]
    public void Deadband_zero_disables_suppression()
    {
        var options = Options(kp: 1, deadband: 0);

        var result = PidController.Step(
            PidControllerState.Initial,
            options,
            setpoint: 10, measurement: 9.999,
            Dt);

        Assert.Equal(0.001, result.Output, precision: 12);
    }

    // --- Determinism / replay ---------------------------------------------

    [Fact]
    public void Step_is_deterministic_for_identical_inputs()
    {
        var options = Options(kp: 1.5, ki: 0.3, kd: 0.7, outputMin: -100, outputMax: 100);
        var initial = PidControllerState.Initial with { PreviousError = 1.0 };

        var a = PidController.Step(initial, options, 10, 4, Dt);
        var b = PidController.Step(initial, options, 10, 4, Dt);

        Assert.Equal(a.Output, b.Output);
        Assert.Equal(a.NextState, b.NextState);
        Assert.Equal(a.WasClamped, b.WasClamped);
        Assert.Equal(a.WasIntegralFrozen, b.WasIntegralFrozen);
    }

    [Fact]
    public void Two_runs_over_a_constant_setpoint_match_bit_exact()
    {
        var options = Options(kp: 1.2, ki: 0.4, kd: 0.1, outputMin: -50, outputMax: 50);

        static List<double> Run(PidControllerOptions opt)
        {
            var outputs = new List<double>();
            var state = PidControllerState.Initial;
            for (var i = 0; i < 50; i++)
            {
                var measurement = i * 0.7;
                var result = PidController.Step(state, opt, setpoint: 20, measurement: measurement, TimeSpan.FromSeconds(0.5));
                outputs.Add(result.Output);
                state = result.NextState;
            }
            return outputs;
        }

        Assert.Equal(Run(options), Run(options));
    }

    // --- Validation --------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    [InlineData(0, double.NaN, 0)]
    [InlineData(0, double.NegativeInfinity, 0)]
    [InlineData(0, 0, double.NaN)]
    public void Non_finite_gains_throw(double kp, double ki, double kd)
    {
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: kp, ki: ki, kd: kd),
            setpoint: 1, measurement: 0, Dt));
    }

    [Fact]
    public void Output_min_above_max_throws()
    {
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1, outputMin: 10, outputMax: 5),
            setpoint: 1, measurement: 0, Dt));
    }

    [Fact]
    public void Negative_deadband_throws()
    {
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1, deadband: -0.1),
            setpoint: 1, measurement: 0, Dt));
    }

    [Fact]
    public void Zero_dt_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1),
            setpoint: 1, measurement: 0, TimeSpan.Zero));
    }

    [Fact]
    public void Negative_dt_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1),
            setpoint: 1, measurement: 0, TimeSpan.FromSeconds(-0.1)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_setpoint_throws(double setpoint)
    {
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1),
            setpoint: setpoint, measurement: 0, Dt));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_measurement_throws(double measurement)
    {
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1),
            setpoint: 0, measurement: measurement, Dt));
    }

    [Fact]
    public void Null_state_throws()
    {
        Assert.Throws<ArgumentNullException>(() => PidController.Step(
            null!,
            Options(kp: 1),
            setpoint: 1, measurement: 0, Dt));
    }

    [Fact]
    public void Null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() => PidController.Step(
            PidControllerState.Initial,
            null!,
            setpoint: 1, measurement: 0, Dt));
    }
}
