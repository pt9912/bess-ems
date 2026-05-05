using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class RampLimiterTests
{
    private static readonly BatteryAsset Asset = new(
        "asset-1", capacityKwh: 100,
        maxChargePowerKw: 50, maxDischargePowerKw: 50,
        minSocPercent: 10, maxSocPercent: 90,
        chargeEfficiency: 0.95, dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 10,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    [Fact]
    public void Ramp_up_within_budget_is_unchanged()
    {
        var result = RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 10,
            requestedActivePowerKw: 18,
            timeSinceLastCommand: TimeSpan.FromSeconds(1));

        Assert.False(result.WasLimited);
        Assert.Equal(18, result.LimitedActivePowerKw);
    }

    [Fact]
    public void Ramp_up_above_budget_is_clamped_to_upper_bound()
    {
        var result = RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 10,
            requestedActivePowerKw: 30,
            timeSinceLastCommand: TimeSpan.FromSeconds(1));

        Assert.True(result.WasLimited);
        Assert.Equal(20, result.LimitedActivePowerKw);
        Assert.Equal("ramp-up-clamped", result.LimitReason);
    }

    [Fact]
    public void Ramp_down_below_budget_is_clamped_to_lower_bound()
    {
        var result = RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 10,
            requestedActivePowerKw: -30,
            timeSinceLastCommand: TimeSpan.FromSeconds(1));

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("ramp-down-clamped", result.LimitReason);
    }

    [Fact]
    public void Ramp_through_zero_is_sign_safe()
    {
        var result = RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 5,
            requestedActivePowerKw: -5,
            timeSinceLastCommand: TimeSpan.FromSeconds(0.5));

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("ramp-down-clamped", result.LimitReason);
    }

    [Fact]
    public void Larger_time_window_allows_larger_step()
    {
        var result = RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 0,
            requestedActivePowerKw: 25,
            timeSinceLastCommand: TimeSpan.FromSeconds(5));

        Assert.False(result.WasLimited);
        Assert.Equal(25, result.LimitedActivePowerKw);
    }

    [Fact]
    public void Negative_time_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 0,
            requestedActivePowerKw: 5,
            timeSinceLastCommand: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Zero_time_with_unchanged_request_is_unchanged()
    {
        var result = RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 12,
            requestedActivePowerKw: 12,
            timeSinceLastCommand: TimeSpan.Zero);

        Assert.False(result.WasLimited);
        Assert.Equal(12, result.LimitedActivePowerKw);
    }

    [Fact]
    public void Zero_time_with_changed_request_is_held_at_previous()
    {
        var result = RampLimiter.Apply(
            Asset,
            previousActivePowerKw: 12,
            requestedActivePowerKw: 14,
            timeSinceLastCommand: TimeSpan.Zero);

        Assert.True(result.WasLimited);
        Assert.Equal(12, result.LimitedActivePowerKw);
        Assert.Equal("ramp-not-permitted", result.LimitReason);
    }
}
