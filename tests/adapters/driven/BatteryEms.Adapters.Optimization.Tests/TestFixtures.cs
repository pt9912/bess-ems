using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Optimization.Tests;

internal static class TestFixtures
{
    public static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    public static BatteryAsset CreateAsset(
        double capacityKwh = 100,
        double maxChargePowerKw = 50,
        double maxDischargePowerKw = 50,
        double minSocPercent = 10,
        double maxSocPercent = 90,
        double chargeEfficiency = 0.95,
        double dischargeEfficiency = 0.95) => new(
        assetId: "asset-1",
        capacityKwh: capacityKwh,
        maxChargePowerKw: maxChargePowerKw,
        maxDischargePowerKw: maxDischargePowerKw,
        minSocPercent: minSocPercent,
        maxSocPercent: maxSocPercent,
        chargeEfficiency: chargeEfficiency,
        dischargeEfficiency: dischargeEfficiency,
        maxRampKwPerSecond: 100,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    internal sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }
}
