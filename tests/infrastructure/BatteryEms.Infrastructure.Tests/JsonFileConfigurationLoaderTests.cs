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
    public void LoadAssets_accepts_single_asset_config_for_backward_compatibility()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var assets = loader.LoadAssets(Path.Combine(ExamplesDirectory, "asset.single-bess.json"));

        var asset = Assert.Single(assets);
        Assert.Equal("single-bess-1", asset.AssetId);
    }

    [Fact]
    public void LoadAssets_loads_multi_asset_config()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var assets = loader.LoadAssets(Path.Combine(ExamplesDirectory, "assets.multi-bess.json"));

        Assert.Equal(["bess-a", "bess-b"], assets.Select(a => a.AssetId).ToArray());
    }

    [Fact]
    public void LoadAssets_rejects_duplicate_asset_ids()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "assets": [
                {
                  "asset_id": "duplicate-bess",
                  "capacity_kwh": 100,
                  "max_charge_power_kw": 50,
                  "max_discharge_power_kw": 50,
                  "min_soc_percent": 10,
                  "max_soc_percent": 90,
                  "charge_efficiency": 0.95,
                  "discharge_efficiency": 0.95,
                  "max_ramp_kw_per_second": 25,
                  "min_operating_temperature_celsius": -20,
                  "max_operating_temperature_celsius": 55
                },
                {
                  "asset_id": "duplicate-bess",
                  "capacity_kwh": 120,
                  "max_charge_power_kw": 60,
                  "max_discharge_power_kw": 60,
                  "min_soc_percent": 15,
                  "max_soc_percent": 85,
                  "charge_efficiency": 0.94,
                  "discharge_efficiency": 0.94,
                  "max_ramp_kw_per_second": 30,
                  "min_operating_temperature_celsius": -20,
                  "max_operating_temperature_celsius": 55
                }
              ]
            }
            """);
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadAssets(path));
            Assert.Contains("duplicate asset_id", ex.Message, StringComparison.Ordinal);
            Assert.Contains("duplicate-bess", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadAssets_rejects_empty_asset_list()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""{ "assets": [] }""");
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadAssets(path));
            Assert.Contains("Schema validation failed", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadAssets_rejects_non_object_root_as_configuration_validation()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""[]""");
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadAssets(path));
            Assert.Contains("Schema validation failed", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
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
    public void Modbus_register_loads_device_point_metadata_when_present()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadModbusMapping(
            Path.Combine(ExamplesDirectory, "adapters", "modbus.simulator.json"));

        // soc_percent in the example carries display_name + unit (LH-DOM-005).
        var soc = mapping.Registers.First(r => r.Name == "soc_percent");
        Assert.NotNull(soc.DevicePoint);
        Assert.Equal("State of charge", soc.DevicePoint!.DisplayName);
        Assert.Equal("%", soc.DevicePoint.Unit);
        Assert.True(soc.DevicePoint.Exportable);

        // temperature_celsius additionally carries an alarm rule.
        var temp = mapping.Registers.First(r => r.Name == "temperature_celsius");
        Assert.NotNull(temp.DevicePoint?.Alarm);
        Assert.Equal(-20, temp.DevicePoint!.Alarm!.Min);
        Assert.Equal(55, temp.DevicePoint.Alarm.Max);
        Assert.Equal("warning", temp.DevicePoint.Alarm.Severity);

        // Registers without any device-point fields surface DevicePoint as null
        // so call sites can quickly tell metadata-rich points from raw ones.
        var setpoint = mapping.Registers.First(r => r.Name == "active_power_setpoint_kw");
        Assert.Null(setpoint.DevicePoint);
    }

    [Fact]
    public void Mqtt_topic_loads_device_point_metadata_when_present()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadMqttMapping(
            Path.Combine(ExamplesDirectory, "adapters", "mqtt.simulator.json"));

        var telemetry = mapping.Topics.First(t => t.Name == "telemetry");
        Assert.NotNull(telemetry.DevicePoint);
        Assert.Equal("Battery telemetry snapshot", telemetry.DevicePoint!.DisplayName);

        // Topics without metadata stay null so the contract matches Modbus.
        var command = mapping.Topics.First(t => t.Name == "command");
        Assert.Null(command.DevicePoint);
    }

    [Fact]
    public void Modbus_register_with_value_explanation_loads_into_device_point()
    {
        // value_explanation is the cross-protocol form of LH-DOM-005's
        // "Werteerklärung". Modbus mappings keep the legacy `enum` field
        // for the register-value map (still tested by the SunSpec profile);
        // value_explanation in device_point_base lets future callers ask
        // the same question without reaching into Modbus-specific fields.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "name": "operating_mode",
                  "display_name": "Inverter operating mode",
                  "address": 200,
                  "type": "uint16",
                  "scale_factor": 1,
                  "range": [0, 3],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none",
                  "value_explanation": { "0": "stop", "1": "idle", "2": "charge", "3": "discharge" }
                }
              ]
            }
            """);
        try
        {
            var mapping = loader.LoadModbusMapping(path);
            var register = Assert.Single(mapping.Registers);
            Assert.NotNull(register.DevicePoint?.ValueExplanation);
            Assert.Equal("discharge", register.DevicePoint!.ValueExplanation!["3"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Modbus_register_with_only_exportable_false_still_surfaces_device_point_metadata()
    {
        // Locks the BuildDevicePoint branch where every other LH-DOM-005
        // metadata field is empty but exportable=false alone counts as
        // "operator opted out of northbound export"; that has to surface
        // a non-null DevicePoint so consumers can tell ignored points
        // apart from points without metadata.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "name": "internal_only",
                  "exportable": false,
                  "address": 300,
                  "type": "uint16",
                  "scale_factor": 1,
                  "range": [0, 1],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var mapping = loader.LoadModbusMapping(path);
            var register = Assert.Single(mapping.Registers);
            Assert.NotNull(register.DevicePoint);
            Assert.False(register.DevicePoint!.Exportable);
            Assert.Null(register.DevicePoint.DisplayName);
            Assert.Null(register.DevicePoint.Unit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("intraday", BatteryEms.Domain.ScheduleType.Intraday)]
    [InlineData("regel_leistung_reserve", BatteryEms.Domain.ScheduleType.RegelLeistungReserve)]
    public void Schedule_loader_accepts_non_day_ahead_types(string wireType, BatteryEms.Domain.ScheduleType expected)
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            $$"""
            {
              "asset_id": "single-bess-1",
              "type": "{{wireType}}",
              "market_bid_area": "DE-LU",
              "version": 1,
              "windows": [
                { "start": "2026-05-06T00:00:00Z", "end": "2026-05-06T01:00:00Z", "target_power_kw": 0 }
              ]
            }
            """);
        try
        {
            var schedule = loader.LoadSchedule(path);
            Assert.Equal(expected, schedule.Type);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Modbus_loader_rejects_non_integer_enum_key()
    {
        // The Modbus mapping schema allows arbitrary string keys in `enum`
        // because JSON object keys are strings; the loader is the layer
        // that enforces "keys must be integer-parseable register values".
        // This locks that boundary.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "name": "operating_mode",
                  "address": 200,
                  "type": "uint16",
                  "scale_factor": 1,
                  "range": [0, 3],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none",
                  "enum": { "0": "stop", "running": "running" }
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Modbus_register_without_required_name_fails_schema_via_device_point_base()
    {
        // device_point_base owns the required-name rule. Without name the
        // mapping must be rejected at schema time, not bubble up as a runtime
        // serialisation error.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "address": 100,
                  "type": "uint16",
                  "scale_factor": 1,
                  "range": [0, 100],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
            Assert.Contains("Schema validation failed", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Mqtt_topic_with_unknown_field_fails_schema()
    {
        // unevaluatedProperties:false (replacing additionalProperties:false)
        // still rejects unknown fields once the device_point_base allOf
        // branch is in place. This locks the contract end-to-end.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "topics": [
                {
                  "name": "telemetry",
                  "topic": "battery/x/telemetry",
                  "direction": "subscribe",
                  "payload_format": "json",
                  "retained": true,
                  "auth_required": "none",
                  "made_up_field": 42
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadMqttMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
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
              "schema_version": "v1",
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
              "schema_version": "v1",
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
    public void Modbus_missing_schema_version_fails_pre_check()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                { "name": "x", "address": 0, "type": "uint16", "scale_factor": 1, "range": [0, 1], "writable": false, "write_cadence": "cyclic", "auth_required": "none" }
              ]
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        Assert.Contains("missing required field 'schema_version'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modbus_unsupported_schema_version_fails_pre_check()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "schema_version": "v2",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                { "name": "x", "address": 0, "type": "uint16", "scale_factor": 1, "range": [0, 1], "writable": false, "write_cadence": "cyclic", "auth_required": "none" }
              ]
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        Assert.Contains("unsupported-schema-version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mqtt_missing_schema_version_fails_pre_check()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "profile_name": "p",
              "topics": [
                { "name": "telemetry", "topic": "battery/x/telemetry", "direction": "subscribe", "payload_format": "json", "retained": true, "auth_required": "none" }
              ]
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadMqttMapping(path));
        Assert.Contains("missing required field 'schema_version'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modbus_non_string_schema_version_fails_structured_not_raw()
    {
        // A numeric schema_version must yield a ConfigurationValidationException,
        // not a raw InvalidOperationException from GetValue<string>().
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "schema_version": 1,
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                { "name": "x", "address": 0, "type": "uint16", "scale_factor": 1, "range": [0, 1], "writable": false, "write_cadence": "cyclic", "auth_required": "none" }
              ]
            }
            """);

        var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        Assert.Contains("required field 'schema_version'", ex.Message, StringComparison.Ordinal);
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

    [Fact]
    public void Loads_basic_day_ahead_schedule_fixture()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var schedule = loader.LoadSchedule(Path.Combine(RepoRoot(), "tests", "fixtures", "schedules", "day-ahead-basic.json"));

        Assert.Equal("single-bess-1", schedule.AssetId);
        Assert.Equal(BatteryEms.Domain.ScheduleType.DayAhead, schedule.Type);
        Assert.Equal("DE-LU", schedule.MarketBidArea);
        Assert.Equal(24, schedule.Windows.Count);
        Assert.Equal(TimeSpan.FromHours(24), schedule.HorizonEnd - schedule.HorizonStart);
    }

    [Fact]
    public void Schedule_loader_resolves_window_at_midday_correctly()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var schedule = loader.LoadSchedule(Path.Combine(RepoRoot(), "tests", "fixtures", "schedules", "day-ahead-basic.json"));

        var midday = new DateTimeOffset(2026, 5, 6, 12, 30, 0, TimeSpan.Zero);
        var window = schedule.WindowCovering(midday);

        Assert.NotNull(window);
        Assert.Equal(30, window!.TargetPowerKw);
    }

    [Fact]
    public void Schedule_loader_handles_dst_spring_forward_continuously_in_utc()
    {
        // LH-MKT-007 acceptance: tests cover at least one summer-time
        // transition. The fixture spans the Europe spring-forward at
        // 2026-03-29T01:00:00Z (= 02:00 CET → 03:00 CEST). UTC is linear
        // across the jump; the loader must reflect that without inventing
        // gaps or duplicates and without the local "missing hour" bleeding
        // into the schedule horizon.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var schedule = loader.LoadSchedule(Path.Combine(RepoRoot(), "tests", "fixtures", "schedules", "day-ahead-dst-transition.json"));

        Assert.Equal(6, schedule.Windows.Count);
        for (var i = 1; i < schedule.Windows.Count; i++)
        {
            Assert.Equal(schedule.Windows[i - 1].End, schedule.Windows[i].Start);
        }

        // Last UTC moment that maps to local CET (02:59:59 CET) — target=10
        // from the fixture's [2026-03-29T00:00:00Z, 01:00:00Z) window.
        var lastCet = new DateTimeOffset(2026, 3, 29, 0, 59, 59, TimeSpan.Zero);
        Assert.Equal(10, schedule.WindowCovering(lastCet)!.TargetPowerKw);

        // First UTC moment after the jump (which the wall clock skips from
        // 02:00 CET to 03:00 CEST). UTC 01:00:00Z is unambiguous and lands
        // in the [01:00:00Z, 02:00:00Z) window with target=20.
        var firstCest = new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(20, schedule.WindowCovering(firstCest)!.TargetPowerKw);
    }

    [Fact]
    public void Schedule_loader_rejects_non_utc_timestamp()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "asset_id": "single-bess-1",
              "type": "day_ahead",
              "market_bid_area": "DE-LU",
              "version": 1,
              "windows": [
                { "start": "2026-05-06T00:00:00+02:00", "end": "2026-05-06T01:00:00+02:00", "target_power_kw": 0 }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadSchedule(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Schedule_loader_rejects_overlapping_windows()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "asset_id": "single-bess-1",
              "type": "day_ahead",
              "market_bid_area": "DE-LU",
              "version": 1,
              "windows": [
                { "start": "2026-05-06T00:00:00Z", "end": "2026-05-06T02:00:00Z", "target_power_kw": 0 },
                { "start": "2026-05-06T01:00:00Z", "end": "2026-05-06T03:00:00Z", "target_power_kw": 0 }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadSchedule(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loads_example_retention_policy_with_audit_omitted()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var policy = loader.LoadRetentionPolicy(Path.Combine(ExamplesDirectory, "retention.json"));

        Assert.Equal(TimeSpan.FromDays(90), policy.TelemetryRetention);
        Assert.Equal(TimeSpan.FromDays(365), policy.CommandsRetention);
        Assert.Equal(TimeSpan.FromDays(30), policy.SchedulesRetention);

        // The example fixture deliberately omits operator_audit_retention so
        // the LH-PERSIST-006 default ("no auto-delete of audit-relevant data
        // without explicit configuration") is what an operator inherits from
        // a copy-paste of the example.
        Assert.Null(policy.OperatorAuditRetention);
    }

    [Fact]
    public void Retention_loader_accepts_audit_when_explicitly_configured()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "telemetry_retention": "30.00:00:00",
              "operator_audit_retention": "1825.00:00:00"
            }
            """);
        try
        {
            var policy = loader.LoadRetentionPolicy(path);
            Assert.Equal(TimeSpan.FromDays(30), policy.TelemetryRetention);
            Assert.Equal(TimeSpan.FromDays(1825), policy.OperatorAuditRetention);
            Assert.Null(policy.CommandsRetention);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Retention_loader_rejects_unknown_field()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "telemetry_retention": "30.00:00:00",
              "made_up_field": "x"
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadRetentionPolicy(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Retention_loader_rejects_malformed_duration()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "telemetry_retention": "P30D"
            }
            """);
        try
        {
            // The schema pattern intentionally rejects ISO-8601 durations
            // in M1 — the loader is locked to the C# TimeSpan format so we
            // only have one parser surface to reason about.
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadRetentionPolicy(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Modbus_register_without_register_table_or_word_order_defaults_to_M1_values()
    {
        // RM-M2-HIL-01: existing M1 profiles do not declare
        // register_table or word_order. The loader must round-trip
        // them as the documented defaults so the M1 simulator path
        // and SunSpec profile keep working without a profile rewrite.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "name": "active_power_kw",
                  "address": 0,
                  "type": "float32",
                  "scale_factor": 1,
                  "range": [-250, 250],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var mapping = loader.LoadModbusMapping(path);
            var register = Assert.Single(mapping.Registers);
            Assert.Equal(ModbusRegisterTables.Holding, register.RegisterTable);
            Assert.Equal(ModbusWordOrders.HighLow, register.WordOrder);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Modbus_register_with_explicit_register_table_and_word_order_round_trips_them()
    {
        // RM-M2-HIL-01: HIL profiles set the new fields per register.
        // The loader copies them into the domain configuration so
        // downstream adapter code (HIL-02 / HIL-03) can branch on
        // them. The schema enum keeps the values restricted.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "name": "active_power_kw",
                  "address": 0,
                  "type": "float32",
                  "register_table": "input",
                  "word_order": "low_high",
                  "scale_factor": 1000,
                  "range": [-250, 250],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var mapping = loader.LoadModbusMapping(path);
            var register = Assert.Single(mapping.Registers);
            Assert.Equal(ModbusRegisterTables.Input, register.RegisterTable);
            Assert.Equal(ModbusWordOrders.LowHigh, register.WordOrder);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loads_HIL_modbus_profile_example()
    {
        // RM-M2-HIL-04: pin the committed example HIL profile against
        // the loader so a future schema or DTO change can't quietly
        // break it. Asserts the HIL-distinctive fields (input-table
        // measurements, low_high float32 word order, MW/MVAR-to-
        // kW/kvar scaling, and a writable holding setpoint).
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadModbusMapping(
            Path.Combine(ExamplesDirectory, "adapters", "modbus.hil-simulator.json"));

        Assert.Equal("bess-hil-simulator", mapping.ProfileName);
        // 5 grid measurements + 4 BESS state regs (available / soc /
        // soh / temperature) + 2 holding setpoints = 11 registers.
        Assert.Equal(11, mapping.Registers.Count);

        var p = mapping.Registers.First(r => r.Name == "active_power_kw");
        Assert.Equal(ModbusRegisterTables.Input, p.RegisterTable);
        Assert.Equal(ModbusWordOrders.LowHigh, p.WordOrder);
        Assert.Equal("float32", p.Type);
        Assert.Equal(1000, p.ScaleFactor);
        Assert.False(p.Writable);

        var setpoint = mapping.Registers.First(r => r.Name == "active_power_setpoint_kw");
        Assert.Equal(ModbusRegisterTables.Holding, setpoint.RegisterTable);
        Assert.True(setpoint.Writable);

        // The closed-loop demo registers must surface the BESS state
        // BatteryTelemetry expects (available + SOC + SOH + temp);
        // skipping any of them sends the control cycle into a
        // safe-stop loop on the HIL stack.
        Assert.Contains(mapping.Registers, r => r.Name == "available");
        Assert.Contains(mapping.Registers, r => r.Name == "soc_percent");
        Assert.Contains(mapping.Registers, r => r.Name == "soh_percent");
        Assert.Contains(mapping.Registers, r => r.Name == "temperature_celsius");
    }

    [Fact]
    public void Modbus_register_with_zero_scale_factor_is_rejected_by_schema()
    {
        // Carve-out N2: scale_factor=0 means engineering_value =
        // raw_value * 0 = 0 (Decode collapses) AND wire_value =
        // engineering_value / 0 = NaN/Inf (Encode breaks). Both are
        // obviously broken; the schema rejects so the profile never
        // reaches the adapter.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "name": "active_power_kw",
                  "address": 0,
                  "type": "float32",
                  "scale_factor": 0,
                  "range": [-250, 250],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Modbus_register_with_unknown_register_table_value_is_rejected_by_schema()
    {
        // RM-M2-HIL-01: schema's enum locks register_table to
        // {holding, input}. A typo or vendor-specific value should
        // never reach the loader's DTO mapping.
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson(
            """
            {
              "schema_version": "v1",
              "profile_name": "p",
              "unit_id_discovery": "static",
              "static_unit_id": 1,
              "registers": [
                {
                  "name": "active_power_kw",
                  "address": 0,
                  "type": "float32",
                  "register_table": "discrete_input",
                  "scale_factor": 1,
                  "range": [-250, 250],
                  "writable": false,
                  "write_cadence": "cyclic",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadModbusMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ----- RM-M4-07 OPC-UA mapping tests --------------------------------

    [Fact]
    public void Loads_example_opcua_simulator_mapping()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadOpcUaMapping(
            Path.Combine(ExamplesDirectory, "adapters", "opcua.simulator.json"));

        Assert.Equal("v1", mapping.SchemaVersion);
        Assert.Equal("opcua-simulator", mapping.ProfileName);
        Assert.NotEmpty(mapping.Nodes);

        var soc = mapping.Nodes.First(n => n.Name == "soc_percent");
        Assert.Equal("ns=2;s=Battery.Soc", soc.NodeId);
        Assert.Equal("subscribe", soc.Direction);
        Assert.Equal("float", soc.DataType);
        Assert.False(soc.Writable);
        Assert.Equal(1000, soc.MonitoringIntervalMs);
        Assert.NotNull(soc.DevicePoint);
        Assert.Equal("State of charge", soc.DevicePoint!.DisplayName);
        Assert.Equal("%", soc.DevicePoint.Unit);

        var setpoint = mapping.Nodes.First(n => n.Name == "active_power_setpoint_kw");
        Assert.Equal("write", setpoint.Direction);
        Assert.True(setpoint.Writable);
        Assert.Equal("cyclic", setpoint.WriteCadence);
        Assert.Equal("token", setpoint.AuthRequired);

        var faultCode = mapping.Nodes.First(n => n.Name == "fault_code");
        Assert.NotNull(faultCode.DevicePoint?.ValueExplanation);
        Assert.Equal("isolation-fault", faultCode.DevicePoint!.ValueExplanation!["1"]);
    }

    [Fact]
    public void Opcua_with_missing_required_field_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        // Missing `data_type` on the only node.
        var path = WriteTempJson("""
            {
              "schema_version": "v1",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "read",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
            Assert.Contains("Schema validation failed", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_zero_scale_factor_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "schema_version": "v1",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "read",
                  "data_type": "float",
                  "scale_factor": 0,
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_malformed_node_id_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        // node_id missing the i=/s=/g=/b= prefix.
        var path = WriteTempJson("""
            {
              "schema_version": "v1",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "Battery.Soc",
                  "direction": "read",
                  "data_type": "float",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_writable_true_but_missing_write_cadence_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "schema_version": "v1",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "write",
                  "data_type": "float",
                  "writable": true,
                  "auth_required": "token"
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_direction_write_but_writable_false_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "schema_version": "v1",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "write",
                  "data_type": "float",
                  "writable": false,
                  "auth_required": "token"
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_unknown_field_fails_schema()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        // Unknown field at the node level — additionalProperties:false
        // + unevaluatedProperties:false must reject it.
        var path = WriteTempJson("""
            {
              "schema_version": "v1",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "read",
                  "data_type": "float",
                  "auth_required": "none",
                  "made_up_field": 42
                }
              ]
            }
            """);
        try
        {
            Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_deprecated_schema_version_v0_surfaces_unsupported_schema_version()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "schema_version": "v0",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "read",
                  "data_type": "float",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
            Assert.Contains("unsupported-schema-version", ex.Message, StringComparison.Ordinal);
            Assert.Contains("v0", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_future_incompatible_schema_version_v2_surfaces_unsupported_schema_version()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "schema_version": "v2",
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "read",
                  "data_type": "float",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
            Assert.Contains("unsupported-schema-version", ex.Message, StringComparison.Ordinal);
            Assert.Contains("v2", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Opcua_with_missing_schema_version_fails_with_dedicated_diagnose()
    {
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var path = WriteTempJson("""
            {
              "profile_name": "p",
              "nodes": [
                {
                  "name": "x",
                  "node_id": "ns=1;i=42",
                  "direction": "read",
                  "data_type": "float",
                  "auth_required": "none"
                }
              ]
            }
            """);
        try
        {
            var ex = Assert.Throws<ConfigurationValidationException>(() => loader.LoadOpcUaMapping(path));
            Assert.Contains("schema_version", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bess-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
