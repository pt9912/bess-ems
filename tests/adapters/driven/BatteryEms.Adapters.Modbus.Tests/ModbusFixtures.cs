using BatteryEms.Adapters.Modbus;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Modbus.Tests;

internal static class ModbusFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    public static BatteryAsset SampleAsset(double maxCharge = 50, double maxDischarge = 50) => new(
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

    public static ModbusMappingConfiguration VendorNeutralMapping() => new(
        ProfileName: "test",
        UnitIdDiscovery: "static",
        StaticUnitId: 1,
        Registers: new List<ModbusRegisterMapping>
        {
            new("soc_percent", 100, "uint16", 0.1, 0, 100, false, "cyclic", "none", null, null, null),
            new("active_power_kw", 110, "int16", 0.1, -100, 100, false, "cyclic", "none", null, null, null),
            new("available", 120, "uint16", 1, 0, 1, false, "cyclic", "none", null, null, null),
            new("active_power_setpoint_kw", 200, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null),
            new("operating_mode", 202, "uint16", 1, 0, 3, true, "cyclic", "none", null, null, null),
        });

    public static ModbusAdapterOptions Defaults() => new(
        Host: "127.0.0.1",
        Port: 5020,
        AssetId: "asset-1",
        PollingInterval: TimeSpan.FromMilliseconds(20),
        ReadTimeout: TimeSpan.FromSeconds(1),
        MaxConsecutiveFailures: 5);

    public sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }
}
