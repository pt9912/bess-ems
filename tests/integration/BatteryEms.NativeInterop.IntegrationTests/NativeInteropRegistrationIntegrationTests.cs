using BatteryEms.Adapters.NativeInterop;
using BatteryEms.Application.Control;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.NativeInterop.IntegrationTests;

// M3-D2 integration test that drives the real
// AddBessNativeControl extension against the production
// libbattery_control_core.so. Mirrors the host's composition
// path: configuration → AddBessNativeControl → IControlKernel
// resolved via the DI container. The asserted contract is that
// `NativeControl:Enabled=true` with a real .so on disk
// registers the NativeFallbackControlKernel — the produktive
// Profilaktivierung gate from plan-RM-M3-D2.md §5.
//
// The unit-level NativeInteropRegistrationTests cover the
// without-library and disabled paths; this integration test
// is specifically about the with-real-library happy path.
[Collection("native-library")]
public sealed class NativeInteropRegistrationIntegrationTests
{
    [Fact]
    public void Enabled_with_real_library_registers_NativeFallbackControlKernel()
    {
        var libraryPath = NativeLibraryLocator.Locate();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NativeControl:Enabled"] = "true",
                ["NativeControl:LibraryPath"] = libraryPath,
            })
            .Build();

        var services = BuildServices(configuration);

        var resolved = services.GetRequiredService<IControlKernel>();

        // The DI factory resolved to the real library handle, so the
        // registered IControlKernel must be the native+fallback
        // adapter — not the bare ManagedControlKernel that every
        // not-Loaded path would yield.
        Assert.IsType<NativeFallbackControlKernel>(resolved);
    }

    [Fact]
    public void Enabled_with_real_library_routes_compute_through_native_kernel()
    {
        // Wire a control input through the registered kernel and
        // confirm the result carries Source=Native, proving the
        // .so is genuinely on the hot path (not just constructed
        // and ignored).
        var libraryPath = NativeLibraryLocator.Locate();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NativeControl:Enabled"] = "true",
                ["NativeControl:LibraryPath"] = libraryPath,
            })
            .Build();
        var services = BuildServices(configuration);
        var kernel = services.GetRequiredService<IControlKernel>();

        var input = new KernelInput(
            Asset: new BatteryEms.Domain.BatteryAsset(
                assetId: "asset-d2",
                capacityKwh: 100,
                maxChargePowerKw: 50,
                maxDischargePowerKw: 50,
                minSocPercent: 10,
                maxSocPercent: 90,
                chargeEfficiency: 0.95,
                dischargeEfficiency: 0.95,
                maxRampKwPerSecond: 25,
                minOperatingTemperatureCelsius: -20,
                maxOperatingTemperatureCelsius: 55),
            Telemetry: new BatteryEms.Domain.BatteryTelemetry(
                Timestamp: DateTimeOffset.UnixEpoch,
                AssetId: "asset-d2",
                SocPercent: 50,
                SohPercent: 100,
                ActivePowerKw: 0,
                ReactivePowerKvar: 0,
                DcVoltage: 800,
                DcCurrent: 0,
                TemperatureCelsius: 22,
                Available: true,
                FaultStatus: "ok",
                DataQuality: BatteryEms.Domain.DataQuality.Valid),
            DispatchTargetActivePowerKw: 10,
            PreviousActivePowerKw: null,
            TimeSinceLastCommand: TimeSpan.FromSeconds(1));

        var result = kernel.Compute(input);

        Assert.Equal(KernelResultSource.Native, result.Source);
        Assert.Equal(10.0, result.ActivePowerKw, precision: 12);
        Assert.Equal("within-limits", result.Reason);
        Assert.False(result.WasLimited);
    }

    [Fact]
    public void Disabled_section_keeps_ManagedControlKernel_even_with_real_library_path()
    {
        // The `LibraryPath` is set to the real .so but `Enabled`
        // stays false — the loader must short-circuit to Disabled
        // and the host gets the managed kernel. Pins the
        // contract that `LibraryPath` alone is not an opt-in
        // signal; only `Enabled=true` activates Native.
        var libraryPath = NativeLibraryLocator.Locate();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NativeControl:Enabled"] = "false",
                ["NativeControl:LibraryPath"] = libraryPath,
            })
            .Build();

        var services = BuildServices(configuration);

        var resolved = services.GetRequiredService<IControlKernel>();

        Assert.IsType<BatteryEms.Application.Control.ManagedControlKernel>(resolved);
    }

    private static IServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddBessNativeControl(configuration);
        return services.BuildServiceProvider();
    }
}
