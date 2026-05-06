using BatteryEms.Application.Assets;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using BatteryEms.Infrastructure.Configuration;

namespace BatteryEms.Host;

// Loads the JSON-file configuration the host needs at start-up and
// surfaces a populated BessRuntimeConfiguration. ConfigurationValidation
// failures crash the start-up — LH-OPS-001 forbids running with missing
// safety bounds, so the boot fails loud rather than silently.
internal static class BessConfigurationBootstrap
{
    public static BessRuntimeConfiguration Load(BessHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SchemaDirectory))
        {
            throw new InvalidOperationException("Bess:SchemaDirectory is required.");
        }
        if (string.IsNullOrWhiteSpace(options.AssetConfigPath))
        {
            throw new InvalidOperationException("Bess:AssetConfigPath is required.");
        }

        var loader = new JsonFileConfigurationLoader(options.SchemaDirectory);
        var asset = loader.LoadAsset(options.AssetConfigPath);

        Schedule? schedule = null;
        if (!string.IsNullOrWhiteSpace(options.ScheduleConfigPath))
        {
            schedule = loader.LoadSchedule(options.ScheduleConfigPath);
        }

        RetentionPolicy? retention = null;
        if (!string.IsNullOrWhiteSpace(options.RetentionConfigPath))
        {
            retention = loader.LoadRetentionPolicy(options.RetentionConfigPath);
        }

        ModbusMappingConfiguration? modbus = null;
        if (!string.IsNullOrWhiteSpace(options.ModbusMappingPath))
        {
            modbus = loader.LoadModbusMapping(options.ModbusMappingPath);
        }

        MqttMappingConfiguration? mqtt = null;
        if (!string.IsNullOrWhiteSpace(options.MqttMappingPath))
        {
            mqtt = loader.LoadMqttMapping(options.MqttMappingPath);
        }

        return new BessRuntimeConfiguration(asset, schedule, retention, modbus, mqtt);
    }

    public static void SeedAssetRegistry(IBatteryAssetRegistry registry, BatteryAsset asset)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(asset);
        if (registry is InMemoryBatteryAssetRegistry inMemory)
        {
            inMemory.Register(asset);
            return;
        }
        throw new InvalidOperationException(
            $"Asset registry of type '{registry.GetType().FullName}' is not seedable from configuration.");
    }

    public static void SeedScheduleRepository(IScheduleRepository repository, Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(schedule);
        repository.Replace(schedule);
    }
}

internal sealed record BessRuntimeConfiguration(
    BatteryAsset Asset,
    Schedule? Schedule,
    RetentionPolicy? Retention,
    ModbusMappingConfiguration? ModbusMapping,
    MqttMappingConfiguration? MqttMapping);
