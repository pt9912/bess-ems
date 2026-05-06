using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Adapters.Mqtt;

// DI extension for the MQTT driven adapter. Telemetry source and
// command sink share the same MQTTnet client (so subscriptions and
// publishes go through one TCP connection); the host loads the topic
// mapping via JsonFileConfigurationLoader and the broker endpoint
// from configuration before calling AddBessMqtt.
public static class MqttRegistration
{
    public static IServiceCollection AddBessMqtt(
        this IServiceCollection services,
        MqttMappingConfiguration mapping,
        MqttAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(mapping);
        services.AddSingleton(options);
        services.AddSingleton<IMqttClient>(_ => new MqttNetClient(options));
        services.AddSingleton<IBatteryTelemetrySource, MqttTelemetrySource>();
        services.AddSingleton<IBatteryCommandSink, MqttCommandSink>();
        return services;
    }
}
