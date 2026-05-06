using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class AdapterWriteLimiterTests
{
    private static BatteryAsset Asset(double maxCharge = 50, double maxDischarge = 50) => new(
        assetId: "asset-1",
        capacityKwh: 100,
        maxChargePowerKw: maxCharge,
        maxDischargePowerKw: maxDischarge,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 25,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private static BatteryCommand Command(CommandMode mode, double activePowerKw, string reason = "test") => new(
        CommandId: "cmd-1",
        Timestamp: DateTimeOffset.UtcNow,
        AssetId: "asset-1",
        Mode: mode,
        ActivePowerKw: activePowerKw,
        ReactivePowerKvar: null,
        ValidUntil: DateTimeOffset.UtcNow.AddSeconds(5),
        Reason: reason,
        Source: CommandSource.Optimization);

    [Fact]
    public void Command_within_asset_limits_passes_through_unchanged()
    {
        var command = Command(CommandMode.Discharge, 25);
        var result = AdapterWriteLimiter.Apply(command, Asset());

        Assert.False(result.WasLimited);
        Assert.Equal("ok", result.Reason);
        Assert.Same(command, result.Command);
    }

    [Fact]
    public void Discharge_above_max_discharge_power_is_clamped_to_asset_limit()
    {
        var result = AdapterWriteLimiter.Apply(Command(CommandMode.Discharge, 100), Asset(maxDischarge: 50));

        Assert.True(result.WasLimited);
        Assert.Equal("max-discharge-power", result.Reason);
        Assert.Equal(50, result.Command.ActivePowerKw);
    }

    [Fact]
    public void Charge_below_negative_max_charge_power_is_clamped_to_asset_limit()
    {
        var result = AdapterWriteLimiter.Apply(Command(CommandMode.Charge, -100), Asset(maxCharge: 40));

        Assert.True(result.WasLimited);
        Assert.Equal("max-charge-power", result.Reason);
        Assert.Equal(-40, result.Command.ActivePowerKw);
    }

    [Fact]
    public void Stop_mode_with_non_zero_power_is_clamped_to_zero()
    {
        var result = AdapterWriteLimiter.Apply(Command(CommandMode.Stop, 25), Asset());

        Assert.True(result.WasLimited);
        Assert.Equal("mode-stop-zero-power", result.Reason);
        Assert.Equal(0, result.Command.ActivePowerKw);
        Assert.Equal(CommandMode.Stop, result.Command.Mode);
    }

    [Fact]
    public void Idle_mode_with_non_zero_power_is_clamped_to_zero()
    {
        var result = AdapterWriteLimiter.Apply(Command(CommandMode.Idle, -10), Asset());

        Assert.True(result.WasLimited);
        Assert.Equal("mode-idle-zero-power", result.Reason);
        Assert.Equal(0, result.Command.ActivePowerKw);
    }

    [Fact]
    public void Stop_mode_with_zero_power_passes_through_unchanged()
    {
        var result = AdapterWriteLimiter.Apply(Command(CommandMode.Stop, 0), Asset());

        Assert.False(result.WasLimited);
        Assert.Equal(0, result.Command.ActivePowerKw);
    }

    [Fact]
    public void Mode_zero_clamp_takes_precedence_over_power_clamp()
    {
        // Stop with 200 kW: mode-zero-power must fire before max-discharge-power
        // would have fired, because the operating mode is the more authoritative
        // signal (an inverter in Stop should not produce any power).
        var result = AdapterWriteLimiter.Apply(Command(CommandMode.Stop, 200), Asset(maxDischarge: 50));

        Assert.True(result.WasLimited);
        Assert.Equal("mode-stop-zero-power", result.Reason);
        Assert.Equal(0, result.Command.ActivePowerKw);
    }

    [Theory]
    [InlineData(50.0)]
    [InlineData(-50.0)]
    public void Boundary_values_pass_through_unchanged(double exactlyAtBoundary)
    {
        var mode = exactlyAtBoundary > 0 ? CommandMode.Discharge : CommandMode.Charge;
        var result = AdapterWriteLimiter.Apply(Command(mode, exactlyAtBoundary), Asset(maxCharge: 50, maxDischarge: 50));

        Assert.False(result.WasLimited);
        Assert.Equal(exactlyAtBoundary, result.Command.ActivePowerKw);
    }

    [Fact]
    public void Apply_throws_for_null_command()
    {
        Assert.Throws<ArgumentNullException>(() => AdapterWriteLimiter.Apply(null!, Asset()));
    }

    [Fact]
    public void Apply_throws_for_null_asset()
    {
        Assert.Throws<ArgumentNullException>(() => AdapterWriteLimiter.Apply(Command(CommandMode.Discharge, 0), null!));
    }
}
