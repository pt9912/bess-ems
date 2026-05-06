using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Adapters.Modbus;

// DI extension for the Modbus driven adapter. The host (RM-M1-19c
// composition root) feeds in the parsed mapping (loaded via
// JsonFileConfigurationLoader) and the network endpoint; AddBessModbus
// registers a single FluentModbusClient as IModbusClient so the
// telemetry source and the command sink share one connection.
public static class ModbusRegistration
{
    public static IServiceCollection AddBessModbus(
        this IServiceCollection services,
        ModbusMappingConfiguration mapping,
        ModbusAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(mapping);
        services.AddSingleton(options);
        services.AddSingleton<IModbusClient>(_ => new FluentModbusClient(options.Host, options.Port));
        services.AddSingleton<IBatteryTelemetrySource, ModbusTelemetrySource>();
        services.AddSingleton<IBatteryCommandSink, ModbusCommandSink>();
        return services;
    }
}
