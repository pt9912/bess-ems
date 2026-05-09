using System.Runtime.InteropServices;
using BatteryEms.Adapters.NativeInterop;
using Xunit;

namespace BatteryEms.NativeInterop.IntegrationTests;

// RM-M3-07 negative-path tests against the real ABI.
//
// The managed precheck (RM-M3-05) is responsible for stopping
// non-finite snapshot/limit/request values BEFORE the routing
// reaches the native kernel — so in production these statuses
// never appear from a validated input. This suite drives the
// native side directly, bypassing KernelInput / Domain
// validation, to prove the C-ABI contract holds: invalid inputs
// return the documented status code instead of returning OK with
// silent garbage or crashing the process.
//
// Parity-with-managed is intentionally NOT asserted here. The
// plan explicitly excludes non-finite / negative-dt / null
// inputs from the Native-vs-Managed parity matrix because the
// managed precheck rejects them before either kernel runs; the
// only contract this suite pins is that the native side does
// not lie to the routing layer when it does see one.
[Collection("native-library")]
public sealed class NativeAbiNegativeTests
{
    private static (BccSnapshot s, BccLimits l, BccRequest r) ValidTriple()
    {
        var s = new BccSnapshot
        {
            SocPercent = 50, ActivePowerKw = 0, TemperatureCelsius = 22,
        };
        var l = new BccLimits
        {
            MaxChargePowerKw = 50, MaxDischargePowerKw = 50,
            MinSocPercent = 10, MaxSocPercent = 90,
            MaxRampKwPerSecond = 25,
            MinTemperatureCelsius = -20, MaxTemperatureCelsius = 55,
        };
        var r = new BccRequest
        {
            TargetActivePowerKw = 10, PreviousActivePowerKw = 0,
            DtSeconds = 1, HasPrevious = 0,
        };
        return (s, l, r);
    }

    [Fact]
    public void Non_finite_snapshot_field_yields_non_finite_status()
    {
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        using var kernel = new NativeControlKernel(handle);

        var (s, l, r) = ValidTriple();
        s.SocPercent = double.NaN;

        var status = kernel.Compute(in s, in l, in r, out var command);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, command.ReasonCode);
        Assert.Equal(0.0, command.ActivePowerKw);
    }

    [Theory]
    [InlineData("MaxChargePowerKw")]
    [InlineData("MaxDischargePowerKw")]
    [InlineData("MaxRampKwPerSecond")]
    [InlineData("MaxTemperatureCelsius")]
    public void Non_finite_limit_field_yields_non_finite_status(string field)
    {
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        using var kernel = new NativeControlKernel(handle);

        var (s, l, r) = ValidTriple();
        switch (field)
        {
            case "MaxChargePowerKw":      l.MaxChargePowerKw      = double.PositiveInfinity; break;
            case "MaxDischargePowerKw":   l.MaxDischargePowerKw   = double.NaN;              break;
            case "MaxRampKwPerSecond":    l.MaxRampKwPerSecond    = double.NegativeInfinity; break;
            case "MaxTemperatureCelsius": l.MaxTemperatureCelsius = double.NaN;              break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }

        var status = kernel.Compute(in s, in l, in r, out var command);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, command.ReasonCode);
    }

    [Fact]
    public void Non_finite_target_power_yields_non_finite_status()
    {
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        using var kernel = new NativeControlKernel(handle);

        var (s, l, r) = ValidTriple();
        r.TargetActivePowerKw = double.NaN;

        var status = kernel.Compute(in s, in l, in r, out var command);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, command.ReasonCode);
    }

    [Fact]
    public void Non_finite_previous_with_has_previous_set_yields_non_finite_status()
    {
        // The native contract treats previous_active_power_kw and
        // dt_seconds as ramp-only inputs: when has_previous == 0
        // they may be NaN (managed M1 first-tick contract). When
        // has_previous == 1 they MUST be finite or the kernel
        // bails out as NON_FINITE.
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        using var kernel = new NativeControlKernel(handle);

        var (s, l, r) = ValidTriple();
        r.HasPrevious = 1;
        r.PreviousActivePowerKw = double.NaN;
        r.DtSeconds = 1;

        var status = kernel.Compute(in s, in l, in r, out var command);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, command.ReasonCode);
    }

    [Fact]
    public void Non_finite_previous_without_has_previous_is_tolerated()
    {
        // First-tick contract: managed code may legitimately pass
        // NaN for previous_active_power_kw to signal "no previous
        // value available". The native kernel mirrors that
        // tolerance — has_previous == 0 means the ramp limiter
        // never inspects previous_active_power_kw, so a NaN there
        // must NOT trip the non-finite guard.
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        using var kernel = new NativeControlKernel(handle);

        var (s, l, r) = ValidTriple();
        r.HasPrevious = 0;
        r.PreviousActivePowerKw = double.NaN;
        r.DtSeconds = double.NaN;
        r.TargetActivePowerKw = 10;

        var status = kernel.Compute(in s, in l, in r, out var command);

        Assert.Equal(BccStatus.Ok, status);
        Assert.Equal(BccReason.WithinLimits, command.ReasonCode);
        Assert.Equal(10.0, command.ActivePowerKw);
    }

    [Fact]
    public void Negative_dt_with_has_previous_yields_negative_dt_status()
    {
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        using var kernel = new NativeControlKernel(handle);

        var (s, l, r) = ValidTriple();
        r.HasPrevious = 1;
        r.PreviousActivePowerKw = 5;
        r.DtSeconds = -1;

        var status = kernel.Compute(in s, in l, in r, out var command);

        Assert.Equal(BccStatus.NegativeDt, status);
        Assert.Equal(BccReason.NegativeDt, command.ReasonCode);
        Assert.Equal(0.0, command.ActivePowerKw);
    }
}
