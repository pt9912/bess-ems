using BatteryEms.Application.Configuration;
using BatteryEms.Infrastructure.Configuration;
using Xunit;

namespace BatteryEms.Infrastructure.Tests;

public sealed class JsonFileConfigurationLoaderTests
{
    private static readonly string SchemaDirectory =
        Path.Combine(RepoRoot(), "config", "schema");

    private static readonly string ExamplesDirectory =
        Path.Combine(RepoRoot(), "config", "examples");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "BatteryEms.sln")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing BatteryEms.sln.");
    }

    [Fact]
    public void Constructor_throws_when_schema_directory_missing()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new JsonFileConfigurationLoader("/nonexistent/path"));
    }

    [Fact]
    public void Loads_example_asset_and_constructs_valid_BatteryAsset()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var asset = loader.LoadAsset(Path.Combine(ExamplesDirectory, "asset.single-bess.json"));

        Assert.Equal("single-bess-1", asset.AssetId);
        Assert.Equal(100, asset.CapacityKwh);
        Assert.Equal(50, asset.MaxChargePowerKw);
        Assert.Equal(0.95, asset.ChargeEfficiency);
    }

    [Fact]
    public void Loads_vendor_neutral_modbus_profile()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadModbusMapping(
            Path.Combine(ExamplesDirectory, "adapters", "modbus.simulator.json"));

        Assert.Equal("vendor-neutral-simulator", mapping.ProfileName);
        Assert.Equal("static", mapping.UnitIdDiscovery);
        Assert.Equal(1, mapping.StaticUnitId);
        Assert.NotEmpty(mapping.Registers);

        var soc = mapping.Registers.First(r => r.Name == "soc_percent");
        Assert.Equal(100, soc.Address);
        Assert.Equal("uint16", soc.Type);
        Assert.False(soc.Writable);
        Assert.Equal(0, soc.RangeMin);
        Assert.Equal(100, soc.RangeMax);

        var setpoint = mapping.Registers.First(r => r.Name == "active_power_setpoint_kw");
        Assert.True(setpoint.Writable);
        Assert.Equal("cyclic", setpoint.WriteCadence);
        Assert.Equal("none", setpoint.AuthRequired);
    }

    [Fact]
    public void Loads_sunspec_profile_with_models_and_heartbeat()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadModbusMapping(
            Path.Combine(ExamplesDirectory, "adapters", "modbus.sunspec-simulator.json"));

        Assert.Equal("sunspec", mapping.UnitIdDiscovery);
        Assert.Null(mapping.StaticUnitId);

        var heartbeat = mapping.Registers.First(r => r.Name == "der_ctl_heartbeat");
        Assert.Equal("heartbeat", heartbeat.WriteCadence);
        Assert.Equal(715, heartbeat.SunspecModel);

        var disconnect = mapping.Registers.First(r => r.Name == "battery_set_op");
        Assert.Equal("cooldown", disconnect.WriteCadence);
        Assert.Equal(802, disconnect.SunspecModel);
        Assert.NotNull(disconnect.Enum);
        Assert.Equal("disconnect", disconnect.Enum![2]);
    }

    [Fact]
    public void Loads_mqtt_profile_with_subscribe_and_publish_topics()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadMqttMapping(
            Path.Combine(ExamplesDirectory, "adapters", "mqtt.simulator.json"));

        Assert.Equal("mqtt-simulator", mapping.ProfileName);
        Assert.Contains(mapping.Topics, t => t.Direction == "publish" && t.Name == "command");
        Assert.Contains(mapping.Topics, t => t.Direction == "subscribe" && t.Name == "command_ack");
    }

    [Fact]
    public void Asset_with_unknown_field_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "asset_id": "x",
              "capacity_kwh": 1,
              "max_charge_power_kw": 1,
              "max_discharge_power_kw": 1,
              "min_soc_percent": 10,
              "max_soc_percent": 90,
              "charge_efficiency": 0.5,
              "discharge_efficiency": 0.5,
              "max_ramp_kw_per_second": 1,
              "min_operating_temperature_celsius": -10,
              "max_operating_temperature_celsius": 40,
              "extra_unknown_field": true
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadAsset(path));
        Assert.Contains("Schema validation failed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Asset_with_min_soc_above_max_fails_domain_invariant()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "asset_id": "x",
              "capacity_kwh": 1,
              "max_charge_power_kw": 1,
              "max_discharge_power_kw": 1,
              "min_soc_percent": 90,
              "max_soc_percent": 50,
              "charge_efficiency": 0.5,
              "discharge_efficiency": 0.5,
              "max_ramp_kw_per_second": 1,
              "min_operating_temperature_celsius": -10,
              "max_operating_temperature_celsius": 40
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadAsset(path));
        Assert.Contains("domain invariants", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modbus_with_invalid_unit_id_discovery_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "profile_name": "p",
              "unit_id_discovery": "magic",
              "registers": [
                { "name": "x", "address": 0, "type": "uint16", "scale_factor": 1, "range": [0, 1], "writable": false, "write_cadence": "cyclic", "auth_required": "none" }
              ]
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        Assert.Contains("Schema validation failed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Static_unit_id_discovery_without_static_unit_id_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "profile_name": "p",
              "unit_id_discovery": "static",
              "registers": [
                { "name": "x", "address": 0, "type": "uint16", "scale_factor": 1, "range": [0, 1], "writable": false, "write_cadence": "cyclic", "auth_required": "none" }
              ]
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        Assert.Contains("Schema validation failed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_file_throws_validation_exception()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var ex = Assert.Throws<ConfigurationValidationException>(() =>
            loader.LoadAsset("/nonexistent/asset.json"));
        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_json_throws_validation_exception()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("{ not valid json");
        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadAsset(path));
        Assert.Contains("not valid JSON", ex.Message, StringComparison.Ordinal);
    }

    private static string WriteTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bess-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
