using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class ConstraintLimiterTests
{
    private static readonly BatteryAsset Asset = new(
        "asset-1", capacityKwh: 100,
        maxChargePowerKw: 50, maxDischargePowerKw: 50,
        minSocPercent: 10, maxSocPercent: 90,
        chargeEfficiency: 0.95, dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 100,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private static BatteryTelemetry Telemetry(
        double socPercent = 50,
        double temperatureCelsius = 22,
        bool available = true)
        => new(
            Timestamp: DateTimeOffset.UnixEpoch,
            AssetId: "asset-1",
            SocPercent: socPercent,
            SohPercent: 100,
            ActivePowerKw: 0,
            ReactivePowerKvar: 0,
            DcVoltage: 800,
            DcCurrent: 0,
            TemperatureCelsius: temperatureCelsius,
            Available: available,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid);

    [Fact]
    public void Unavailable_asset_is_clamped_to_zero()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(available: false), requestedActivePowerKw: 30);

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("asset-unavailable", result.LimitReason);
    }

    [Theory]
    [InlineData(-21)]
    [InlineData(56)]
    public void Out_of_range_temperature_is_clamped_to_zero(double temperature)
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(temperatureCelsius: temperature), 25);

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("temperature-out-of-range", result.LimitReason);
    }

    [Fact]
    public void Charging_blocked_at_or_above_max_soc()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(socPercent: 90), requestedActivePowerKw: -25);

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("soc-at-max-charge-blocked", result.LimitReason);
    }

    [Fact]
    public void Discharging_blocked_at_or_below_min_soc()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(socPercent: 10), requestedActivePowerKw: 25);

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("soc-at-min-discharge-blocked", result.LimitReason);
    }

    [Fact]
    public void Charging_request_below_max_charge_power_is_clamped()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(), requestedActivePowerKw: -75);

        Assert.True(result.WasLimited);
        Assert.Equal(-50, result.LimitedActivePowerKw);
        Assert.Equal("max-charge-power", result.LimitReason);
    }

    [Fact]
    public void Discharging_request_above_max_discharge_power_is_clamped()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(), requestedActivePowerKw: 75);

        Assert.True(result.WasLimited);
        Assert.Equal(50, result.LimitedActivePowerKw);
        Assert.Equal("max-discharge-power", result.LimitReason);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(-25)]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    public void Within_limits_request_passes_through(double request)
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(), request);

        Assert.False(result.WasLimited);
        Assert.Equal(request, result.LimitedActivePowerKw);
    }
}
