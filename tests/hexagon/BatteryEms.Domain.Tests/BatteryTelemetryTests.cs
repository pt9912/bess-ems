using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class BatteryTelemetryTests
{
    [Fact]
    public void DataQuality_Valid_is_usable_for_control()
    {
        Assert.Equal(DataQualityState.Valid, DataQuality.Valid.Flag);
        Assert.True(DataQuality.Valid.IsUsableForControl);
    }

    [Fact]
    public void DataQuality_Stale_is_not_usable_for_control()
    {
        var quality = DataQuality.Stale("snapshot-aged");
        Assert.Equal(DataQualityState.Stale, quality.Flag);
        Assert.Equal("snapshot-aged", quality.Reason);
        Assert.False(quality.IsUsableForControl);
    }

    [Fact]
    public void DataQuality_Substituted_is_not_usable_for_control()
    {
        var quality = DataQuality.Substituted("bms-fallback");
        Assert.Equal(DataQualityState.Substituted, quality.Flag);
        Assert.False(quality.IsUsableForControl);
    }

    [Fact]
    public void DataQuality_ProtocolError_is_not_usable_for_control()
    {
        var quality = DataQuality.ProtocolError("modbus-timeout");
        Assert.Equal(DataQualityState.ProtocolError, quality.Flag);
        Assert.False(quality.IsUsableForControl);
    }

    [Fact]
    public void Telemetry_carries_all_fields()
    {
        var ts = new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);
        var telemetry = new BatteryTelemetry(
            Timestamp: ts,
            AssetId: "asset-1",
            SocPercent: 50,
            SohPercent: 99,
            ActivePowerKw: 25,
            ReactivePowerKvar: 5,
            DcVoltage: 800,
            DcCurrent: 31.25,
            TemperatureCelsius: 22,
            Available: true,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid);

        Assert.Equal(ts, telemetry.Timestamp);
        Assert.Equal("asset-1", telemetry.AssetId);
        Assert.Equal(50, telemetry.SocPercent);
        Assert.Equal(25, telemetry.ActivePowerKw);
        Assert.True(telemetry.Available);
    }
}
