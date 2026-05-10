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

        OpcUaMappingConfiguration? opcua = null;
        if (!string.IsNullOrWhiteSpace(options.OpcUaMappingPath))
        {
            opcua = loader.LoadOpcUaMapping(options.OpcUaMappingPath);
        }

        return new BessRuntimeConfiguration(asset, schedule, retention, modbus, mqtt, opcua);
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
        // Bootstrap-Seed runs on every host start. With persistence
        // wired, the schedule row from the previous boot is still in
        // Postgres, so a hard-coded `expectedBaseVersion: 0` would
        // fail CAS on every restart. Read the active row's version
        // first; on first boot it is null (insert path), on restart
        // it carries the persisted version (CAS-update path).
        //
        // Multi-replica cold-start race: two hosts booting in parallel
        // can both see `existing == null`, both attempt the insert
        // path, and the loser's `INSERT ... ON CONFLICT DO NOTHING`
        // surfaces a ScheduleConcurrencyConflictException. The seed's
        // contract is "ensure a schedule is present at startup"; that
        // is satisfied by the sibling's write, so swallowing the
        // conflict keeps the loser's host alive instead of crashing
        // it. The migrator above relies on pg_advisory_lock for the
        // same class of race; the seed does not because the typed
        // exception lets us tell "lost the race" apart from any other
        // failure, so a Postgres-specific lock is unnecessary here.
        var existing = repository.FindActive(schedule.AssetId, schedule.Type);
        try
        {
            repository.Replace(schedule, expectedBaseVersion: existing?.Version ?? 0);
        }
        catch (ScheduleConcurrencyConflictException)
        {
            // Sibling replica seeded first. Idempotent semantics — leave
            // their schedule in place and continue startup.
        }
    }
}

internal sealed record BessRuntimeConfiguration(
    BatteryAsset Asset,
    Schedule? Schedule,
    RetentionPolicy? Retention,
    ModbusMappingConfiguration? ModbusMapping,
    MqttMappingConfiguration? MqttMapping,
    OpcUaMappingConfiguration? OpcUaMapping);
