using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-B Fixture-Defaults: Adapter- + Request-Builder für
// die TestSidecar-Integration-Pins. Profile=HilSimulator damit
// EnsureValid plaintext-http-/UDS-Endpoints akzeptiert.
internal static class Defaults
{
    public static OptimizationCoreOptions ForHilSimulator(Uri endpoint) =>
        new()
        {
            SidecarEndpoint = endpoint,
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestDeadline = TimeSpan.FromSeconds(10),
            ExpectedContractVersion = "1.0.0",
            RequiredFeatures = new[] { "has-usable-solution" },
        };

    public static BatteryAsset SampleAsset() => new(
        assetId: "asset-m5-test",
        capacityKwh: 100,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 10,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    public static ScheduleOptimizationRequest SampleRequest(
        DateTimeOffset horizonStartUtc,
        TimeSpan horizon,
        TimeSpan timeStep,
        int baseScheduleVersion = 0,
        string marketBidArea = "DE-LU")
    {
        var asset = SampleAsset();
        var stepCount = (int)(horizon.Ticks / timeStep.Ticks);
        var prices = new double[stepCount];
        for (var i = 0; i < stepCount; i++) { prices[i] = 50.0; }
        var command = new ScheduleOptimizationCommand(
            assetId: asset.AssetId,
            scheduleType: ScheduleType.DayAhead,
            asset: asset,
            horizonStart: horizonStartUtc,
            horizonEnd: horizonStartUtc + horizon,
            timeStep: timeStep,
            pricesPerStep: prices,
            priceUnit: "EUR/MWh",
            inputs: Array.Empty<ScheduleReference>());
        return new ScheduleOptimizationRequest(
            command,
            marketBidArea: marketBidArea,
            baseScheduleVersion: baseScheduleVersion);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
        Justification = "Used by tests as IClock impl.")]
    internal sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } =
            new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    }
}
