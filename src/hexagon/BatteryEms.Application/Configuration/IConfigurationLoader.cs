using BatteryEms.Domain;

namespace BatteryEms.Application.Configuration;

public interface IConfigurationLoader
{
    BatteryAsset LoadAsset(string filePath);

    ModbusMappingConfiguration LoadModbusMapping(string filePath);

    MqttMappingConfiguration LoadMqttMapping(string filePath);

    OpcUaMappingConfiguration LoadOpcUaMapping(string filePath);

    Schedule LoadSchedule(string filePath);

    RetentionPolicy LoadRetentionPolicy(string filePath);
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException() { }

    public ConfigurationValidationException(string message) : base(message) { }

    public ConfigurationValidationException(string message, Exception inner) : base(message, inner) { }
}
