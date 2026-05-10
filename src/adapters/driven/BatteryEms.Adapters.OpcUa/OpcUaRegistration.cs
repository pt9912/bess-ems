using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OpcUa;

// DI-Erweiterung für den OPC-UA-Adapter (plan-RM-M4-04 §4 Sub-Slice C).
// Der Host wiring (`BessHostBuilder.BuildApp`) ruft `AddBessOpcUa`
// genau dann, wenn der Operator das OPC-UA-Mapping + Endpoint
// konfiguriert hat (multi-IO-fail-closed-Triage in Sub-Slice C5).
//
// Lifecycle: ein einziger `IOpcUaClient`-Singleton wird zwischen
// Telemetry-Source und Command-Sink geteilt — beide erwarten denselben
// Session-State (D-09 Lifecycle-Vertrag). Container-Shutdown disposed
// den Client; Source + Sink markieren sich nur self-disposed (siehe
// Klassen-Header dort).
//
// **Production-Stub-Warnung** (review-fix #5): die heutige
// `OpcUaClient`-Implementierung wirft auf jeder Operation
// `NotImplementedException` — die echte SDK-Bindung kommt erst mit
// Sub-Slice D. Beim ersten IOpcUaClient-Resolve emittiert die
// Registry-Factory eine strukturierte Warning (EventId 4220), damit
// der Operator das Stub-Verhalten im stdout-Log sieht statt erst
// beim ersten gescheiterten Telemetrie-Tick zu rätseln.
public static partial class OpcUaRegistration
{
    public static IServiceCollection AddBessOpcUa(
        this IServiceCollection services,
        OpcUaMappingConfiguration mapping,
        OpcUaAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(mapping);
        services.AddSingleton(options);
        services.AddSingleton<IOpcUaClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<OpcUaClient>>();
            LogProductionStubInUse(logger, options.EndpointUrl);
            return new OpcUaClient(options);
        });
        services.AddSingleton<IBatteryTelemetrySource, OpcUaTelemetrySource>();
        services.AddSingleton<IBatteryCommandSink, OpcUaCommandSink>();
        return services;
    }

    [LoggerMessage(EventId = 4220, Level = LogLevel.Warning,
        Message = "opcua adapter is wired with the build-time stub against {EndpointUrl}; the first ConnectAsync/ReadAsync/WriteAsync call will throw NotImplementedException until RM-M4-04-D ships the SDK binding. Tests should inject FakeOpcUaClient via the Sub-Slice-A test stub.")]
    private static partial void LogProductionStubInUse(ILogger logger, Uri endpointUrl);
}
