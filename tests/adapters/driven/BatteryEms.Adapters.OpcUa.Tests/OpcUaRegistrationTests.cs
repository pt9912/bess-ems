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

    private static OpcUaAdapterOptions Options() => new()
    {
        EndpointUrl = new Uri("opc.tcp://localhost:4840"),
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
            // Replace the production-stub IOpcUaClient with the
            // test fake so the resolved Source/Sink are usable.
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

    [Fact]
    public void Resolves_telemetry_source_as_opcua_telemetry_source()
    {
        // Inject the FakeOpcUaClient so the source's ctor works
        // (production stub doesn't fail at ctor either, but a fake
        // keeps the service-provider state usable in the assertion).
        using var sp = (ServiceProvider)BuildProvider(new FakeOpcUaClient());

        var resolved = sp.GetRequiredService<IBatteryTelemetrySource>();
        Assert.IsType<OpcUaTelemetrySource>(resolved);
    }

    [Fact]
    public void Resolves_command_sink_as_opcua_command_sink()
    {
        using var sp = (ServiceProvider)BuildProvider(new FakeOpcUaClient());

        var resolved = sp.GetRequiredService<IBatteryCommandSink>();
        Assert.IsType<OpcUaCommandSink>(resolved);
    }

    // Plan-RM-M4-04 review-fix #5: das Production-Stub-Verhalten muss
    // beim ersten IOpcUaClient-Resolve eine Warning emittieren, sodass
    // der Operator das im stdout-Log sieht — sonst rätselt man erst
    // beim ersten gescheiterten Telemetrie-Tick mit "where does this
    // NotImplementedException come from".
    [Fact]
    public void Resolving_production_client_emits_stub_warning()
    {
        var spyProvider = new SpyLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(spyProvider));
        services.AddSingleton<BatteryEms.Application.Time.IClock, TestClock>();
        services.AddSingleton(Asset);
        services.AddBessOpcUa(Mapping(), Options());

        using var sp = services.BuildServiceProvider();
        // Trigger the IOpcUaClient factory.
        _ = sp.GetRequiredService<IOpcUaClient>();

        Assert.Contains(spyProvider.Records,
            r => r.Level == LogLevel.Warning && r.EventId == 4220);
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

    private sealed class SpyLoggerProvider : ILoggerProvider
    {
        public List<LogRecord> Records { get; } = new();
        public ILogger CreateLogger(string categoryName) => new SpyLogger(this);
        public void Dispose() { }
    }

    private sealed class SpyLogger : ILogger
    {
        private readonly SpyLoggerProvider _provider;
        public SpyLogger(SpyLoggerProvider provider) { _provider = provider; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state)!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _provider.Records.Add(new LogRecord(
                logLevel, eventId.Id, formatter(state, exception)));
        }
    }
}

internal sealed record LogRecord(
    Microsoft.Extensions.Logging.LogLevel Level,
    int EventId,
    string Message);
