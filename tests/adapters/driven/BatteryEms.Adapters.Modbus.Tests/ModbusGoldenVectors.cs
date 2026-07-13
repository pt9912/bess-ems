using System.Text.Json;
using System.Text.Json.Nodes;
using BatteryEms.Application.Configuration;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Modbus.Tests;

// ADR 0013 §5.4: Modbus golden vectors — register wire images per shipped
// mapping profile, lifted through the C# codec: RegisterDecoder.Encode for
// read registers (the contract's reference implementation) and the REAL
// ModbusCommandSink dispatch for write registers (FakeModbusClient captures
// the holding writes). Values come from per-profile tables whose RAW values
// (value / scale_factor) are float32-exact resp. integral, so
// Decode(words) == value holds exactly (plan decision 3).
//
// The profile JSON is bound directly into ModbusRegisterMapping: absent
// register_table/word_order fall back to the record's init defaults — the
// SAME defaults the production loader applies, so no default value is
// duplicated here.
internal static class ModbusGoldenVectors
{
    public const string ReadDescription =
        "Field-served read register; words lifted through RegisterDecoder.Encode, Decode(words) == value exactly.";

    public const string WriteDescription =
        "EMS write image lifted from the real ModbusCommandSink dispatch (unclamped: value inside the asset limits).";

    public static readonly IReadOnlyList<string> ProfileFiles = new[]
    {
        "modbus.simulator.json",
        "modbus.hil-simulator.json",
    };

    public static string ManifestPath(string profileFile) =>
        Path.Combine(RepoRoot(), "config", "schema", "vectors",
            $"modbus-golden-vectors.{ProfileKey(profileFile)}.v1.json");

    public static string ProfileKey(string profileFile) =>
        profileFile.Split('.')[1];

    private static IReadOnlyDictionary<string, double> Values(string profileFile) => profileFile switch
    {
        "modbus.simulator.json" => new Dictionary<string, double>
        {
            ["soc_percent"] = 60.5,
            ["soh_percent"] = 99.0,
            ["active_power_kw"] = -25.5,
            ["reactive_power_kvar"] = 12.5,
            ["dc_voltage"] = 800.0,
            ["dc_current"] = -313.1,
            ["temperature_celsius"] = 22.0,
            ["available"] = 1.0,
            ["fault_status"] = 0.0,
            ["active_power_setpoint_kw"] = 25.5,
            ["operating_mode"] = 3.0,
        },
        "modbus.hil-simulator.json" => new Dictionary<string, double>
        {
            ["active_power_kw"] = 62.5,
            ["reactive_power_kvar"] = 31.25,
            ["grid_voltage_pu"] = 1.0,
            ["grid_frequency_hz"] = 50.0,
            ["grid_current_ka"] = 0.5,
            ["available"] = 1.0,
            ["soc_percent"] = 60.5,
            ["soh_percent"] = 99.0,
            ["temperature_celsius"] = 22.0,
            ["active_power_setpoint_kw"] = 31.25,
            ["reactive_power_setpoint_kvar"] = 15.625,
        },
        _ => throw new ArgumentException($"unknown profile '{profileFile}'"),
    };

    // The write-case set is asymmetric per profile (plan sub-slice 2): the
    // simulator profile has an active setpoint + operating_mode but no Q
    // register (command carries ReactivePowerKvar=null so nothing q-drops);
    // the HIL profile has both setpoints and no mode register. Discharge
    // keeps AdapterWriteLimiter from zeroing the power (Stop/Idle contract).
    private static BatteryCommand WriteCommand(string profileFile)
    {
        var values = Values(profileFile);
        return new BatteryCommand(
            CommandId: "cmd-golden-modbus",
            Timestamp: ModbusFixtures.Now,
            AssetId: "asset-1",
            Mode: CommandMode.Discharge,
            ActivePowerKw: values["active_power_setpoint_kw"],
            ReactivePowerKvar: values.TryGetValue("reactive_power_setpoint_kvar", out var q) ? q : null,
            ValidUntil: ModbusFixtures.Now.AddMinutes(1),
            Reason: "golden-vector-dispatch",
            Source: CommandSource.Schedule);
    }

    public static async Task<string> GenerateManifestJsonAsync(string profileFile)
    {
        var mapping = LoadProfile(profileFile);
        var values = Values(profileFile);
        var writeWords = await CaptureWriteWordsAsync(mapping, WriteCommand(profileFile));

        var cases = new JsonArray();
        foreach (var register in mapping.Registers)
        {
            if (register.Type == "string")
            {
                // Schema promise: string registers carry no golden vectors
                // (no numeric wire image; RegisterDecoder does not encode
                // them). Review finding 7: filter here AND in the coverage
                // test so a future string register fails loud in neither.
                continue;
            }

            var value = values[register.Name];
            var direction = register.Writable ? "write" : "read";
            var words = register.Writable
                ? writeWords[register.Address]
                : RegisterDecoder.Encode(register, value);
            cases.Add(new JsonObject
            {
                ["name"] = $"{register.Name}-{direction}",
                ["register"] = register.Name,
                ["direction"] = direction,
                ["register_table"] = register.RegisterTable,
                ["address"] = register.Address,
                ["type"] = register.Type,
                ["word_order"] = register.WordOrder,
                ["scale_factor"] = register.ScaleFactor,
                ["value"] = value,
                ["words"] = new JsonArray(words.Select(w => (JsonNode)JsonValue.Create((int)w)).ToArray()),
                ["description"] = register.Writable ? WriteDescription : ReadDescription,
            });
        }

        var manifest = new JsonObject
        {
            ["schema_version"] = "golden-vector-manifest.v1",
            ["contract"] = "modbus",
            ["authority"] = "ems",
            ["profile"] = profileFile,
            ["cases"] = cases,
        };
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task<IReadOnlyDictionary<int, ushort[]>> CaptureWriteWordsAsync(
        ModbusMappingConfiguration mapping, BatteryCommand command)
    {
        var client = new FakeModbusClient();
        var sink = new ModbusCommandSink(client, mapping, ModbusFixtures.SampleAsset(), ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock());
        var result = await sink.WriteAsync(command, CancellationToken.None);
        if (!result.Success)
        {
            throw new InvalidOperationException($"golden write dispatch failed: {result.Reason}");
        }

        return client.Writes.ToDictionary(w => w.Address, w => w.Values);
    }

    // Binds a shipped profile JSON directly into the Application mapping
    // records. Only fields present in the JSON are applied; the record's
    // init defaults (RegisterTable=holding, WordOrder=high_low) are the
    // production loader defaults.
    public static ModbusMappingConfiguration LoadProfile(string profileFile)
    {
        var path = Path.Combine(RepoRoot(), "config", "examples", "adapters", profileFile);
        var root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"profile did not parse: {path}");

        var registers = new List<ModbusRegisterMapping>();
        foreach (var node in root["registers"]!.AsArray())
        {
            var reg = node!.AsObject();
            var range = reg["range"]!.AsArray();
            var mapping = new ModbusRegisterMapping(
                Name: reg["name"]!.GetValue<string>(),
                Address: reg["address"]!.GetValue<int>(),
                Type: reg["type"]!.GetValue<string>(),
                ScaleFactor: reg["scale_factor"]!.GetValue<double>(),
                RangeMin: range[0]!.GetValue<double>(),
                RangeMax: range[1]!.GetValue<double>(),
                Writable: reg["writable"]!.GetValue<bool>(),
                WriteCadence: reg["write_cadence"]!.GetValue<string>(),
                AuthRequired: reg["auth_required"]!.GetValue<string>(),
                Enum: reg["enum"] is JsonObject enumMap
                    ? enumMap.ToDictionary(
                        p => int.Parse(p.Key, System.Globalization.CultureInfo.InvariantCulture),
                        p => p.Value!.GetValue<string>())
                    : null,
                FirmwareConstraint: reg["firmware_constraint"]?.GetValue<string>(),
                SunspecModel: reg["sunspec_model"]?.GetValue<int>());
            if (reg["register_table"] is JsonNode table)
            {
                mapping = mapping with { RegisterTable = table.GetValue<string>() };
            }

            if (reg["word_order"] is JsonNode order)
            {
                mapping = mapping with { WordOrder = order.GetValue<string>() };
            }

            registers.Add(mapping);
        }

        return new ModbusMappingConfiguration(
            SchemaVersion: root["schema_version"]!.GetValue<string>(),
            ProfileName: root["profile_name"]!.GetValue<string>(),
            UnitIdDiscovery: root["unit_id_discovery"]!.GetValue<string>(),
            StaticUnitId: root["static_unit_id"]?.GetValue<int>(),
            Registers: registers);
    }

    public static string RepoRoot()
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
}
