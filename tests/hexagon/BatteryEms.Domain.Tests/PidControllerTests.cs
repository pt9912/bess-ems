using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class PidControllerTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1);

    // Wide finite bounds for tests that don't care about clamping.
    // Using ±double.MaxValue would be the literal "effectively
    // unbounded" option but invites clamping false-positives if a test
    // ever produces a near-MaxValue intermediate; 1e10 is plenty for
    // anything the test fixtures generate.
    private const double UnboundedMin = -1e10;
    private const double UnboundedMax = 1e10;

    private static PidControllerOptions Options(
        double kp = 0,
        double ki = 0,
        double kd = 0,
        double outputMin = UnboundedMin,
        double outputMax = UnboundedMax,
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
        // candidate past OutputMax while the integrator step has the
        // same sign as the saturation direction.
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
        // because the integrator step (Ki>0, error>0) would relieve,
        // not worsen, the saturation.
        var options = Options(ki: 1.0, outputMin: -5);
        var state = PidControllerState.Initial with { Integral = -20 };

        var result = PidController.Step(state, options, setpoint: 10, measurement: 0, Dt);

        Assert.False(result.WasIntegralFrozen);
        Assert.Equal(-10.0, result.NextState.Integral, precision: 12);
    }

    [Fact]
    public void Anti_windup_uses_integrator_step_sign_not_error_sign_with_negative_ki()
    {
        // With Ki < 0 the integrator decrements when error > 0. If the
        // output is saturated at OutputMax with positive error, the
        // integrator update relieves saturation — anti-windup must NOT
        // freeze. The pre-fix logic gated on `error > 0` alone and
        // would have frozen here, locking the integrator out of its
        // legitimate decrement.
        var options = Options(ki: -0.1, outputMax: 5);
        var state = PidControllerState.Initial with { Integral = 10.0 };

        var step = PidController.Step(state, options, setpoint: 10, measurement: 0, Dt);

        Assert.False(step.WasIntegralFrozen);
        Assert.Equal(9.0, step.NextState.Integral, precision: 12);
    }

    [Fact]
    public void Anti_windup_freezes_at_lower_bound_when_negative_ki_with_positive_error_pushes_down()
    {
        // Symmetric to the above: Ki<0, error>0 → integrator
        // decrements. If the candidate already sits below OutputMin,
        // the decrement worsens saturation and must be frozen.
        var options = Options(ki: -0.1, outputMin: -5);
        var state = PidControllerState.Initial with { Integral = -10.0 };

        var step = PidController.Step(state, options, setpoint: 10, measurement: 0, Dt);

        Assert.True(step.WasIntegralFrozen);
        Assert.Equal(-10.0, step.NextState.Integral, precision: 12);
    }

    // --- Deadband ----------------------------------------------------------

    [Fact]
    public void Deadband_suppresses_p_and_holds_integrator_for_small_errors()
    {
        var options = Options(kp: 5, ki: 1, kd: 1, deadband: 1.5);
        // Warm-start with a non-zero previous error to verify the
        // kernel does NOT zero PreviousError on deadband entry; that
        // would create a derivative kick (and break the post-band
        // derivative test below).
        var warm = PidControllerState.Initial with { PreviousError = 7.0, Integral = 3.0 };

        var result = PidController.Step(warm, options, setpoint: 10, measurement: 9.5, Dt);

        // Inside deadband: P=0, D suppressed to 0, integrator held.
        // Output = held integrator = 3.
        Assert.Equal(3.0, result.Output);
        Assert.Equal(3.0, result.NextState.Integral);
        // PreviousError preserved (7.0), not overwritten.
        Assert.Equal(7.0, result.NextState.PreviousError);
    }

    [Fact]
    public void Deadband_entry_does_not_produce_derivative_kick()
    {
        // Without deadband-D-suppression, entering the deadband with a
        // nonzero previous error would compute D = Kd*(0 - prevErr)/dt,
        // producing a spike. Verify that does not happen.
        var options = Options(kp: 0, ki: 0, kd: 5, deadband: 1.0);
        var warm = PidControllerState.Initial with { PreviousError = 8.0, Integral = 2.0 };

        var result = PidController.Step(warm, options, setpoint: 10, measurement: 9.5, Dt);

        // P=0, D=0 (suppressed), I held. Output = I = 2.
        Assert.Equal(2.0, result.Output);
    }

    [Fact]
    public void Deadband_exit_uses_last_real_error_for_derivative()
    {
        // After spending time in the deadband, the next out-of-band
        // step computes the derivative against the LAST REAL error
        // (preserved through the deadband sojourn), not against zero.
        var options = Options(kp: 0, ki: 0, kd: 1, deadband: 1.0);
        var state = PidControllerState.Initial with { PreviousError = 5.0 };

        // Step 1: error = 0.5, in deadband → PreviousError stays 5.0,
        // D suppressed.
        state = PidController.Step(state, options, setpoint: 10, measurement: 9.5, Dt).NextState;
        Assert.Equal(5.0, state.PreviousError);

        // Step 2: error = 8 (out of deadband). D = 1 * (8 - 5) / 1 = 3.
        var exit = PidController.Step(state, options, setpoint: 10, measurement: 2, Dt);
        Assert.Equal(3.0, exit.Output);
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

        // Now error is well inside the deadband — output holds at the
        // previously accumulated integral, not collapsed.
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
    public void Step_sequence_matches_golden_trace()
    {
        // Bit-exact golden trace. Inputs are chosen so every
        // intermediate is exactly representable in IEEE 754 binary
        // floats (integers and halves only); any change to the kernel
        // math that alters the output sequence breaks this test.
        var options = new PidControllerOptions
        {
            Kp = 1, Ki = 0.5, Kd = 2,
            OutputMin = -100, OutputMax = 100,
        };

        double[] measurements = [0, 5, 8, 10, 9, 7];
        double[] expected = [35.0, 2.5, 4.5, 4.5, 12.0, 17.5];

        var state = PidControllerState.Initial;
        for (var i = 0; i < measurements.Length; i++)
        {
            var result = PidController.Step(state, options, setpoint: 10, measurement: measurements[i], Dt);
            Assert.Equal(expected[i], result.Output);
            state = result.NextState;
        }
    }

    // --- Validation: gains -------------------------------------------------

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

    // --- Validation: output bounds (required + finite) --------------------

    [Fact]
    public void Output_min_above_max_throws()
    {
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial,
            Options(kp: 1, outputMin: 10, outputMax: 5),
            setpoint: 1, measurement: 0, Dt));
    }

    [Fact]
    public void Non_finite_output_min_throws()
    {
        var options = new PidControllerOptions
        {
            Kp = 1, OutputMin = double.NegativeInfinity, OutputMax = 10,
        };
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial, options, 1, 0, Dt));
    }

    [Fact]
    public void Non_finite_output_max_throws()
    {
        var options = new PidControllerOptions
        {
            Kp = 1, OutputMin = -10, OutputMax = double.PositiveInfinity,
        };
        Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial, options, 1, 0, Dt));
    }

    [Fact]
    public void Nan_output_max_throws_with_correct_param_name()
    {
        // Diagnosability: ParamName must point at the actual offender,
        // not always at OutputMin (which the previous implementation
        // did because the NaN check was a single combined branch).
        var options = new PidControllerOptions
        {
            Kp = 1, OutputMin = -10, OutputMax = double.NaN,
        };
        var ex = Assert.Throws<ArgumentException>(() => PidController.Step(
            PidControllerState.Initial, options, 1, 0, Dt));
        Assert.Equal("OutputMax", ex.ParamName);
    }

    // --- Validation: deadband / dt / inputs -------------------------------

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

    // --- Validation: state non-finite -------------------------------------

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_state_integral_throws(double integral)
    {
        var state = PidControllerState.Initial with { Integral = integral };
        Assert.Throws<ArgumentException>(() => PidController.Step(
            state, Options(kp: 1), setpoint: 1, measurement: 0, Dt));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_state_previous_error_throws(double prevError)
    {
        var state = PidControllerState.Initial with { PreviousError = prevError };
        Assert.Throws<ArgumentException>(() => PidController.Step(
            state, Options(kp: 1), setpoint: 1, measurement: 0, Dt));
    }

    // --- Validation: overflow on computed pre-clamp -----------------------

    [Fact]
    public void Pre_clamp_overflow_throws_overflow_exception()
    {
        // Construct a setup that forces the D term to overflow:
        // very large Kd, very large error change, very small dt.
        // d = 1e300 * (1e300 - (-1e300)) / 1e-7 = 2e607, non-finite.
        var options = new PidControllerOptions
        {
            Kp = 0, Ki = 0, Kd = 1e300,
            OutputMin = -1e308, OutputMax = 1e308,
        };
        var state = PidControllerState.Initial with { PreviousError = -1e300 };

        Assert.Throws<OverflowException>(() => PidController.Step(
            state, options,
            setpoint: 1e300, measurement: 0,
            TimeSpan.FromTicks(1)));
    }

    // --- Validation: null arguments ---------------------------------------

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
