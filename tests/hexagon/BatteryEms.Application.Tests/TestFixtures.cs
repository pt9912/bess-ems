using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.Tests;

internal static class TestFixtures
{
    public static readonly DateTimeOffset Now =
        new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    public static BatteryAsset CreateAsset(string assetId = "asset-1") => new(
        assetId: assetId,
        capacityKwh: 100,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 100,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    public static BatteryTelemetry CreateTelemetry(
        string assetId = "asset-1",
        double socPercent = 50,
        double activePowerKw = 0,
        double temperatureCelsius = 22,
        bool available = true,
        DataQuality? quality = null)
        => new(
            Timestamp: Now,
            AssetId: assetId,
            SocPercent: socPercent,
            SohPercent: 100,
            ActivePowerKw: activePowerKw,
            ReactivePowerKvar: 0,
            DcVoltage: 800,
            DcCurrent: 0,
            TemperatureCelsius: temperatureCelsius,
            Available: available,
            FaultStatus: "ok",
            DataQuality: quality ?? DataQuality.Valid);
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = TestFixtures.Now;
}
