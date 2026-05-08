using BatteryEms.Application.Control;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

// RM-M3-05 unit tests for the managed kernel. Each scenario maps
// onto a row of the M3-Akzeptanzdaten table and onto an existing
// Constraint/Ramp safety test — the kernel must reproduce the
// pre-port behaviour bit-for-bit so the cycle's parity oracle is
// preserved.
public sealed class ManagedControlKernelTests
{
    private static readonly ManagedControlKernel Kernel = new();

    private static KernelInput Input(
        double dispatch,
        double? previous = null,
        double dt = 1.0,
        double soc = 50,
        double temp = 22)
    {
        return new KernelInput(
            Asset: TestFixtures.CreateAsset(),
            Telemetry: TestFixtures.CreateTelemetry(socPercent: soc, temperatureCelsius: temp),
            DispatchTargetActivePowerKw: dispatch,
            PreviousActivePowerKw: previous,
            TimeSinceLastCommand: TimeSpan.FromSeconds(dt));
    }

    [Fact]
    public void Within_limits_no_previous_returns_target_with_within_limits_reason()
    {
        var result = Kernel.Compute(Input(dispatch: 25));
        Assert.Equal(25, result.ActivePowerKw);
        Assert.False(result.WasLimited);
        Assert.Equal("within-limits", result.Reason);
        Assert.Equal(KernelResultSource.Managed, result.Source);
    }

    [Fact]
    public void Max_discharge_clamp_yields_constraint_reason()
    {
        // TestFixtures asset: max discharge 50, max charge 50.
        var result = Kernel.Compute(Input(dispatch: 200));
        Assert.Equal(50, result.ActivePowerKw);
        Assert.True(result.WasLimited);
        Assert.Equal("max-discharge-power", result.Reason);
    }

    [Fact]
    public void Soc_at_min_blocks_discharge()
    {
        // TestFixtures asset MinSoc=10; SOC=5 below it.
        var result = Kernel.Compute(Input(dispatch: 30, soc: 5));
        Assert.Equal(0, result.ActivePowerKw);
        Assert.True(result.WasLimited);
        Assert.Equal("soc-at-min-discharge-blocked", result.Reason);
    }

    [Fact]
    public void Ramp_clamps_when_previous_is_within_step()
    {
        // TestFixtures MaxRamp=100 kW/s, dt=0.1 → max delta 10.
        // previous=10, target=30 → upper bound 20, clamped to 20.
        var result = Kernel.Compute(Input(dispatch: 30, previous: 10, dt: 0.1));
        Assert.Equal(20, result.ActivePowerKw);
        Assert.True(result.WasLimited);
        Assert.Equal("ramp-up-clamped", result.Reason);
    }

    [Fact]
    public void Constraint_and_ramp_combined_keeps_constraint_reason()
    {
        // dispatch=200 (constraint clamps to 50) + ramp from
        // previous=10 with dt=0.1, max_ramp=100 → ramp upper=20, so
        // final value=20 (ramp clamps the constrained 50 down to
        // 20). Reason = max-discharge-power (constraint wins).
        var result = Kernel.Compute(Input(dispatch: 200, previous: 10, dt: 0.1));
        Assert.Equal(20, result.ActivePowerKw);
        Assert.True(result.WasLimited);
        Assert.Equal("max-discharge-power", result.Reason);
    }

    [Fact]
    public void First_tick_skips_ramp_even_when_target_far_from_zero()
    {
        // No previous power → ramp limiter is bypassed; constraint-
        // only result. Mirrors RampLimiter's first-tick contract.
        var result = Kernel.Compute(Input(dispatch: 40, previous: null));
        Assert.Equal(40, result.ActivePowerKw);
        Assert.False(result.WasLimited);
        Assert.Equal("within-limits", result.Reason);
    }

    [Fact]
    public void Constructor_param_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Kernel.Compute(null!));
    }
}
