using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

public sealed class OpcUaRegistrationTests
{
    private static readonly Domain.BatteryAsset Asset = new(
        assetId: "asset-1",
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

    // M4-05-A: Test-Defaults bleiben im None+AllowUnsecured-Pfad mit
    // RuntimeProfile=HilSimulator (siehe OpcUaAdapterOptions D-02).
    private static OpcUaAdapterOptions Options() => new()
    {
        EndpointUrl = new Uri("opc.tcp://localhost:4840"),
        RuntimeProfile = OpcUaRuntimeProfile.HilSimulator,
        SecurityMode = OpcUaSecurityMode.None,
        AllowUnsecured = true,
        AllowUnsecuredReason = "registration-tests",
    };

    private static OpcUaMappingConfiguration Mapping() => new(
        SchemaVersion: "v1",
        ProfileName: "test",
        Nodes:
        [
            new OpcUaNodeMapping(
                Name: "soc_percent",
                NodeId: "ns=2;Soc",
                Direction: "read",
                DataType: "float",
                ScaleFactor: 1.0,
                Writable: false,
                AuthRequired: "none"),
            new OpcUaNodeMapping(
                Name: "active_power_setpoint_kw",
                NodeId: "ns=2;Setpoint",
                Direction: "write",
                DataType: "float",
                ScaleFactor: 1.0,
                Writable: true,
                AuthRequired: "none",
                WriteCadence: "cyclic"),
        ]);

    private static IServiceProvider BuildProvider(IOpcUaClient? clientOverride = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<BatteryEms.Application.Time.IClock, TestClock>();
        services.AddSingleton(Asset);
        services.AddBessOpcUa(Mapping(), Options());
        if (clientOverride is not null)
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(IOpcUaClient))
                {
                    services.RemoveAt(i);
                }
            }
            services.AddSingleton(clientOverride);
        }
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_mapping_and_options_as_singletons()
    {
        using var sp = (ServiceProvider)BuildProvider();

        Assert.NotNull(sp.GetRequiredService<OpcUaMappingConfiguration>());
        Assert.NotNull(sp.GetRequiredService<OpcUaAdapterOptions>());
    }

    // M4-05-A inline-fix: `await using` statt `using` — die resolved
    // Singletons (OpcUaTelemetrySource/Sink/Client) implementieren nur
    // IAsyncDisposable. ServiceProvider.Dispose() (sync) wirft
    // InvalidOperationException für IAsyncDisposable-only-Typen;
    // `DisposeAsync` ist der korrekte Pfad.
    [Fact]
    public async Task Resolves_telemetry_source_as_opcua_telemetry_source()
    {
        await using var sp = (ServiceProvider)BuildProvider(new FakeOpcUaClient());

        var resolved = sp.GetRequiredService<IBatteryTelemetrySource>();
        Assert.IsType<OpcUaTelemetrySource>(resolved);
    }

    [Fact]
    public async Task Resolves_command_sink_as_opcua_command_sink()
    {
        await using var sp = (ServiceProvider)BuildProvider(new FakeOpcUaClient());

        var resolved = sp.GetRequiredService<IBatteryCommandSink>();
        Assert.IsType<OpcUaCommandSink>(resolved);
    }

    [Fact]
    public void Constructor_null_args_throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OpcUaRegistration.AddBessOpcUa(null!, Mapping(), Options()));
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddBessOpcUa(null!, Options()));
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddBessOpcUa(Mapping(), null!));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
        Justification = "Instantiated by the DI container via reflection in BuildProvider.")]
    private sealed class TestClock : BatteryEms.Application.Time.IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
