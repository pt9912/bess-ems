using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class BatteryAssetTests
{
    private static BatteryAsset CreateValid(string assetId = "asset-1") => new(
        assetId: assetId,
        capacityKwh: 100,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 25,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    [Fact]
    public void Construction_with_valid_parameters_succeeds()
    {
        var asset = CreateValid();
        Assert.Equal("asset-1", asset.AssetId);
        Assert.Equal(100, asset.CapacityKwh);
        Assert.Equal(50, asset.MaxChargePowerKw);
        Assert.Equal(50, asset.MaxDischargePowerKw);
        Assert.Equal(0.95, asset.ChargeEfficiency);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AssetId_must_be_non_empty(string assetId) =>
        Assert.Throws<ArgumentException>(() => new BatteryAsset(
            assetId, 100, 50, 50, 10, 90, 0.95, 0.95, 25, -20, 55));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CapacityKwh_must_be_positive(double capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", capacity, 50, 50, 10, 90, 0.95, 0.95, 25, -20, 55));

    [Fact]
    public void Negative_max_charge_power_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, -1, 50, 10, 90, 0.95, 0.95, 25, -20, 55));

    [Fact]
    public void Negative_max_discharge_power_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, 50, -1, 10, 90, 0.95, 0.95, 25, -20, 55));

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, 50)]
    [InlineData(60, 50)]
    public void Soc_bounds_must_satisfy_min_lt_max(double min, double max) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, 50, 50, min, max, 0.95, 0.95, 25, -20, 55));

    [Fact]
    public void MaxSoc_above_100_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, 50, 50, 10, 101, 0.95, 0.95, 25, -20, 55));

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void Charge_efficiency_must_be_in_open_zero_one_closed(double eff) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, 50, 50, 10, 90, eff, 0.95, 25, -20, 55));

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void Discharge_efficiency_must_be_in_open_zero_one_closed(double eff) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, 50, 50, 10, 90, 0.95, eff, 25, -20, 55));

    [Fact]
    public void Negative_max_ramp_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, 50, 50, 10, 90, 0.95, 0.95, -1, -20, 55));

    [Theory]
    [InlineData(20, 20)]
    [InlineData(30, 20)]
    public void Operating_temperature_min_must_be_lt_max(double min, double max) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryAsset(
            "asset-1", 100, 50, 50, 10, 90, 0.95, 0.95, 25, min, max));

    [Fact]
    public void Equality_is_by_asset_id()
    {
        var a = CreateValid("asset-1");
        var b = CreateValid("asset-1");
        var c = CreateValid("asset-2");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Equals_is_false_against_null_and_other_types()
    {
        var asset = CreateValid();
        Assert.False(asset.Equals(null));
        Assert.False(asset.Equals("not-an-asset"));
        Assert.False(asset.Equals(new object()));
    }
}
