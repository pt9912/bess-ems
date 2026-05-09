using System.Runtime.InteropServices;
using BatteryEms.Adapters.NativeInterop;
using Xunit;

namespace BatteryEms.NativeInterop.IntegrationTests;

// RM-M3-13 PID negative-path integration tests against the real
// libbattery_control_core.so. Mirror of NativeAbiNegativeTests for
// the Constraint+Ramp kernel — proves that the documented status /
// reason mapping survives the P/Invoke boundary for every error
// path the doctest unit suite covers natively.
//
// Parity (Native vs Managed) is intentionally NOT asserted here;
// the managed reference (BatteryEms.Domain.PidController.Step)
// throws on every input that produces these statuses, so a
// happy-path dataset would be a different gate. RM-M3-FUP could
// add a PID parity dataset if a future host wires the native PID
// into the regulation cycle (M3-D2).
[Collection("native-library")]
public sealed class NativeAbiPidNegativeTests
{
    private static (BccPidState s, BccPidOptions o, BccPidInput i) ValidTriple()
    {
        var s = new BccPidState
        {
            Integral      = 0.0,
            PreviousError = 0.0,
        };
        var o = new BccPidOptions
        {
            Kp                = 1.0,
            Ki                = 0.5,
            Kd                = 0.1,
            OutputMin         = -100.0,
            OutputMax         = 100.0,
            DeadbandAbsolute  = 0.0,
            AntiWindupMode    = BccPidAntiWindupMode.ConditionalIntegration,
        };
        var i = new BccPidInput
        {
            Setpoint     = 10.0,
            Measurement  = 0.0,
            DtSeconds    = 1.0,
        };
        return (s, o, i);
    }

    private static NativeControlKernel LoadKernel()
    {
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        return new NativeControlKernel(handle);
    }

    [Fact]
    public void Pid_happy_path_returns_OK_and_within_limits()
    {
        // Wire-test that the export resolves and the marshalled
        // struct round-trips. Math is doctest-covered natively.
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.Ok, status);
        Assert.Equal(BccReason.WithinLimits, cmd.ReasonCode);
        Assert.Equal(0, cmd.WasClamped);
        Assert.Equal(0, cmd.WasIntegralFrozen);
        // pre-clamp = Kp*10 + Ki*10*dt + Kd*(10-0)/dt = 10 + 5 + 1 = 16
        Assert.Equal(16.0, cmd.Output, precision: 12);
        Assert.Equal(5.0,  cmd.NextIntegral, precision: 12);
        Assert.Equal(10.0, cmd.NextPreviousError, precision: 12);
    }

    [Fact]
    public void Pid_dt_zero_yields_NEGATIVE_DT()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        i.DtSeconds = 0.0;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.NegativeDt, status);
        Assert.Equal(BccReason.NegativeDt, cmd.ReasonCode);
    }

    [Fact]
    public void Pid_negative_dt_yields_NEGATIVE_DT()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        i.DtSeconds = -1.0;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.NegativeDt, status);
        Assert.Equal(BccReason.NegativeDt, cmd.ReasonCode);
    }

    [Theory]
    [InlineData("Setpoint")]
    [InlineData("Measurement")]
    [InlineData("DtSeconds")]
    public void Pid_non_finite_input_yields_NON_FINITE(string field)
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        switch (field)
        {
            case "Setpoint":    i.Setpoint    = double.NaN; break;
            case "Measurement": i.Measurement = double.PositiveInfinity; break;
            case "DtSeconds":   i.DtSeconds   = double.NegativeInfinity; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, cmd.ReasonCode);
    }

    [Theory]
    [InlineData("Integral")]
    [InlineData("PreviousError")]
    public void Pid_non_finite_state_yields_NON_FINITE(string field)
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        switch (field)
        {
            case "Integral":      s.Integral      = double.NaN; break;
            case "PreviousError": s.PreviousError = double.PositiveInfinity; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, cmd.ReasonCode);
    }

    [Theory]
    [InlineData("Kp")]
    [InlineData("Ki")]
    [InlineData("Kd")]
    [InlineData("OutputMin")]
    [InlineData("OutputMax")]
    [InlineData("DeadbandAbsolute")]
    public void Pid_non_finite_option_yields_NON_FINITE(string field)
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        switch (field)
        {
            case "Kp":               o.Kp               = double.NaN; break;
            case "Ki":               o.Ki               = double.PositiveInfinity; break;
            case "Kd":               o.Kd               = double.NegativeInfinity; break;
            case "OutputMin":        o.OutputMin        = double.NaN; break;
            case "OutputMax":        o.OutputMax        = double.NaN; break;
            case "DeadbandAbsolute": o.DeadbandAbsolute = double.PositiveInfinity; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, cmd.ReasonCode);
    }

    [Fact]
    public void Pid_output_min_greater_than_max_yields_INVALID_INPUT_with_PID_INVALID_OPTIONS()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        o.OutputMin = 100.0;
        o.OutputMax = 1.0;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.InvalidInput, status);
        Assert.Equal(BccPidReason.InvalidOptions, cmd.ReasonCode);
    }

    [Fact]
    public void Pid_negative_deadband_yields_INVALID_INPUT_with_PID_INVALID_OPTIONS()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        o.DeadbandAbsolute = -0.001;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.InvalidInput, status);
        Assert.Equal(BccPidReason.InvalidOptions, cmd.ReasonCode);
    }

    [Fact]
    public void Pid_unknown_anti_windup_mode_yields_UNSUPPORTED_STATE()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        o.AntiWindupMode = 99;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.UnsupportedState, status);
        Assert.Equal(BccReason.UnsupportedState, cmd.ReasonCode);
    }

    [Fact]
    public void Pid_integrator_overflow_yields_NON_FINITE_with_PID_INTEGRATOR_OVERFLOW()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        o.Ki = double.MaxValue;
        i.Setpoint = double.MaxValue;
        i.DtSeconds = 2.0;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccPidReason.IntegratorOverflow, cmd.ReasonCode);
    }

    [Fact]
    public void Pid_output_clamping_high_yields_LIMITED_with_PID_OUTPUT_CLAMPED_HIGH()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        o.OutputMin = -1.0;
        o.OutputMax = 1.0;
        o.Kp = 10.0;
        o.Ki = 0.0;
        o.Kd = 0.0;
        i.Setpoint = 100.0;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.Limited, status);
        Assert.Equal(BccPidReason.OutputClampedHigh, cmd.ReasonCode);
        Assert.Equal(1.0, cmd.Output, precision: 12);
        Assert.Equal(1, cmd.WasClamped);
    }

    [Fact]
    public void Pid_output_clamping_low_yields_LIMITED_with_PID_OUTPUT_CLAMPED_LOW()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        o.OutputMin = -1.0;
        o.OutputMax = 1.0;
        o.Kp = 10.0;
        o.Ki = 0.0;
        o.Kd = 0.0;
        i.Setpoint = -100.0;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.Limited, status);
        Assert.Equal(BccPidReason.OutputClampedLow, cmd.ReasonCode);
        Assert.Equal(-1.0, cmd.Output, precision: 12);
    }

    [Fact]
    public void Pid_anti_windup_freezes_integrator_at_high_saturation()
    {
        using var kernel = LoadKernel();
        var (s, o, i) = ValidTriple();
        o.OutputMin = -1.0;
        o.OutputMax = 1.0;
        o.Kp = 10.0;
        o.Ki = 1.0;
        o.Kd = 0.0;
        s.Integral = 0.5;
        i.Setpoint = 100.0;

        var status = kernel.PidStep(in s, in o, in i, out var cmd);

        Assert.Equal(BccStatus.Limited, status);
        Assert.Equal(1, cmd.WasIntegralFrozen);
        Assert.Equal(0.5, cmd.NextIntegral, precision: 12);
    }
}
