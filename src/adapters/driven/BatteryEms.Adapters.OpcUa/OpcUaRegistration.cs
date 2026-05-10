using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using Microsoft.Extensions.DependencyInjection;

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
public static class OpcUaRegistration
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
        services.AddSingleton<IOpcUaClient>(_ => new OpcUaClient(options));
        services.AddSingleton<IBatteryTelemetrySource, OpcUaTelemetrySource>();
        services.AddSingleton<IBatteryCommandSink, OpcUaCommandSink>();
        return services;
    }
}
