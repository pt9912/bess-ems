using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BatteryEms.Application.Configuration;
using BatteryEms.Domain;
using Json.Schema;

namespace BatteryEms.Infrastructure.Configuration;

public sealed class JsonFileConfigurationLoader : IConfigurationLoader
{
    // RM-M4-07 D-02: only v1 ships today. Adding v2+ requires a
    // tested migration path (see follow-up F-07 in
    // note-RM-M4-followups.md). The loader pre-validates this list
    // before the JSON-schema run so an old/new file format gets a
    // structured "unsupported-schema-version" diagnose instead of a
    // generic enum-violation message.
    private static readonly string[] SupportedOpcUaSchemaVersions = ["v1"];

    private readonly JsonSchema _assetSchema;
    private readonly JsonSchema _assetsSchema;
    private readonly JsonSchema _modbusSchema;
    private readonly JsonSchema _mqttSchema;
    private readonly JsonSchema _opcuaSchema;
    private readonly JsonSchema _scheduleSchema;
    private readonly JsonSchema _retentionSchema;
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonFileConfigurationLoader(string schemaDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDirectory);
        if (!Directory.Exists(schemaDirectory))
        {
            throw new DirectoryNotFoundException($"Schema directory not found: {schemaDirectory}");
        }

        // LH-DOM-005 device-point base lives in its own schema and is
        // referenced from both the Modbus and MQTT mapping schemas.
        // JsonSchema.Net 8.0 changed Build/FromFile to auto-register
        // schemas in SchemaRegistry.Global via their $id (the
        // device-point.schema.json carries
        // $id="https://bess-ems.io/schema/device-point.json"), so an
        // explicit Register call is redundant — and worse, it now
        // throws on the second loader instance in the same process.
        // The path-keyed schema cache below ensures FromFile runs once
        // per file, period.
        _ = LoadSchema(schemaDirectory, "device-point.schema.json");

        _assetSchema = LoadSchema(schemaDirectory, "asset.schema.json");
        _assetsSchema = LoadSchema(schemaDirectory, "assets.schema.json");
        _modbusSchema = LoadSchema(schemaDirectory, "modbus-mapping.schema.json");
        _mqttSchema = LoadSchema(schemaDirectory, "mqtt-mapping.schema.json");
        _opcuaSchema = LoadSchema(schemaDirectory, "opcua-mapping.schema.json");
        _scheduleSchema = LoadSchema(schemaDirectory, "schedule.schema.json");
        _retentionSchema = LoadSchema(schemaDirectory, "retention.schema.json");

        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
    }

    public BatteryAsset LoadAsset(string filePath)
    {
        var node = LoadAndValidate(filePath, _assetSchema);
        return BuildAsset(filePath, node);
    }

    public IReadOnlyList<BatteryAsset> LoadAssets(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var rawNode = LoadJson(filePath);
        if (rawNode is JsonObject root && root.ContainsKey("assets"))
        {
            var node = ValidateNode(filePath, rawNode, _assetsSchema);
            if (node is not JsonObject validatedRoot || validatedRoot["assets"] is not JsonArray assetsNode)
            {
                throw new ConfigurationValidationException($"{filePath} has no asset list.");
            }

            var assets = new List<BatteryAsset>(assetsNode.Count);
            foreach (var item in assetsNode)
            {
                if (item is null)
                {
                    throw new ConfigurationValidationException($"{filePath} contains an empty asset entry.");
                }
                assets.Add(BuildAsset(filePath, item));
            }

            var duplicates = assets
                .GroupBy(a => a.AssetId, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                throw new ConfigurationValidationException(
                    $"{filePath} contains duplicate asset_id values: {string.Join(", ", duplicates)}.");
            }

            return assets;
        }

        return [BuildAsset(filePath, ValidateNode(filePath, rawNode, _assetSchema))];
    }

    private static BatteryAsset BuildAsset(string filePath, JsonNode node)
    {
        try
        {
            return new BatteryAsset(
                assetId: GetRequiredString(node, "asset_id"),
                capacityKwh: GetRequiredDouble(node, "capacity_kwh"),
                maxChargePowerKw: GetRequiredDouble(node, "max_charge_power_kw"),
                maxDischargePowerKw: GetRequiredDouble(node, "max_discharge_power_kw"),
                minSocPercent: GetRequiredDouble(node, "min_soc_percent"),
                maxSocPercent: GetRequiredDouble(node, "max_soc_percent"),
                chargeEfficiency: GetRequiredDouble(node, "charge_efficiency"),
                dischargeEfficiency: GetRequiredDouble(node, "discharge_efficiency"),
                maxRampKwPerSecond: GetRequiredDouble(node, "max_ramp_kw_per_second"),
                minOperatingTemperatureCelsius: GetRequiredDouble(node, "min_operating_temperature_celsius"),
                maxOperatingTemperatureCelsius: GetRequiredDouble(node, "max_operating_temperature_celsius"));
        }
        catch (ArgumentException ex)
        {
            throw new ConfigurationValidationException($"Asset configuration violates domain invariants: {ex.Message}", ex);
        }
    }

    public ModbusMappingConfiguration LoadModbusMapping(string filePath)
    {
        var node = LoadAndValidate(filePath, _modbusSchema);

        var dto = node.Deserialize<ModbusMappingDto>(_serializerOptions)
            ?? throw new ConfigurationValidationException($"Failed to deserialize {filePath} as Modbus mapping.");

        if (dto.Registers is null)
        {
            throw new ConfigurationValidationException($"{filePath} has no register list.");
        }

        var registers = new List<ModbusRegisterMapping>(dto.Registers.Count);
        foreach (var r in dto.Registers)
        {
            if (r.Range is null || r.Range.Length != 2)
            {
                throw new ConfigurationValidationException($"{filePath} register '{r.Name}' has malformed range.");
            }

            registers.Add(new ModbusRegisterMapping(
                Name: r.Name,
                Address: r.Address,
                Type: r.Type,
                ScaleFactor: r.ScaleFactor,
                RangeMin: r.Range[0],
                RangeMax: r.Range[1],
                Writable: r.Writable,
                WriteCadence: r.WriteCadence,
                AuthRequired: r.AuthRequired,
                Enum: r.Enum is null ? null : ConvertEnum(r.Enum, filePath, r.Name),
                FirmwareConstraint: r.FirmwareConstraint,
                SunspecModel: r.SunspecModel)
            {
                DevicePoint = BuildDevicePoint(r.DisplayName, r.Unit, r.Exportable, r.Alarm, r.ValueExplanation),
                RegisterTable = r.RegisterTable ?? ModbusRegisterTables.Holding,
                WordOrder = r.WordOrder ?? ModbusWordOrders.HighLow,
            });
        }

        return new ModbusMappingConfiguration(
            ProfileName: dto.ProfileName,
            UnitIdDiscovery: dto.UnitIdDiscovery,
            StaticUnitId: dto.StaticUnitId,
            Registers: registers,
            SchemaVersion: dto.SchemaVersion ?? "v1");
    }

    public MqttMappingConfiguration LoadMqttMapping(string filePath)
    {
        var node = LoadAndValidate(filePath, _mqttSchema);

        var dto = node.Deserialize<MqttMappingDto>(_serializerOptions)
            ?? throw new ConfigurationValidationException($"Failed to deserialize {filePath} as MQTT mapping.");

        if (dto.Topics is null)
        {
            throw new ConfigurationValidationException($"{filePath} has no topic list.");
        }

        var topics = dto.Topics
            .Select(t => new MqttTopicMapping(t.Name, t.Topic, t.Direction, t.PayloadFormat, t.Retained, t.AuthRequired)
            {
                DevicePoint = BuildDevicePoint(t.DisplayName, t.Unit, t.Exportable, t.Alarm, t.ValueExplanation),
            })
            .ToList();

        return new MqttMappingConfiguration(dto.ProfileName, topics, dto.SchemaVersion ?? "v1");
    }

    public OpcUaMappingConfiguration LoadOpcUaMapping(string filePath)
    {
        // RM-M4-07: pre-validate `schema_version` for a structured
        // diagnose path. JSON-schema validation also enforces the
        // enum, but its error message would be generic. By reading
        // the field first we surface "unsupported-schema-version"
        // with the actual value plus the supported set — operator-
        // actionable signal beats opaque schema-validation noise.
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new ConfigurationValidationException($"Configuration file not found: {filePath}");
        }

        JsonNode? rawNode;
        try
        {
            rawNode = JsonNode.Parse(File.ReadAllText(filePath));
        }
        catch (JsonException ex)
        {
            throw new ConfigurationValidationException($"Configuration file is not valid JSON: {filePath}", ex);
        }
        if (rawNode is null)
        {
            throw new ConfigurationValidationException($"Configuration file is empty: {filePath}");
        }

        var declaredVersion = rawNode["schema_version"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(declaredVersion))
        {
            throw new ConfigurationValidationException(
                $"OPC-UA mapping {filePath} is missing required field 'schema_version'.");
        }
        if (!SupportedOpcUaSchemaVersions.Contains(declaredVersion, StringComparer.Ordinal))
        {
            throw new ConfigurationValidationException(
                $"OPC-UA mapping {filePath} declares unsupported-schema-version '{declaredVersion}'; supported: [{string.Join(", ", SupportedOpcUaSchemaVersions)}].");
        }

        var node = LoadAndValidate(filePath, _opcuaSchema);

        var dto = node.Deserialize<OpcUaMappingDto>(_serializerOptions)
            ?? throw new ConfigurationValidationException($"Failed to deserialize {filePath} as OPC-UA mapping.");

        if (dto.Nodes is null)
        {
            throw new ConfigurationValidationException($"{filePath} has no node list.");
        }

        var nodes = dto.Nodes
            .Select(n => new OpcUaNodeMapping(
                Name: n.Name,
                NodeId: n.NodeId,
                Direction: n.Direction,
                DataType: n.DataType,
                ScaleFactor: n.ScaleFactor ?? 1.0,
                Writable: n.Writable ?? false,
                AuthRequired: n.AuthRequired,
                WriteCadence: n.WriteCadence,
                MonitoringIntervalMs: n.MonitoringIntervalMs)
            {
                DevicePoint = BuildDevicePoint(n.DisplayName, n.Unit, n.Exportable, n.Alarm, n.ValueExplanation),
            })
            .ToList();

        return new OpcUaMappingConfiguration(dto.SchemaVersion, dto.ProfileName, nodes);
    }

    public RetentionPolicy LoadRetentionPolicy(string filePath)
    {
        var node = LoadAndValidate(filePath, _retentionSchema);

        var dto = node.Deserialize<RetentionPolicyDto>(_serializerOptions)
            ?? throw new ConfigurationValidationException($"Failed to deserialize {filePath} as retention policy.");

        try
        {
            return new RetentionPolicy(
                TelemetryRetention: dto.TelemetryRetention,
                CommandsRetention: dto.CommandsRetention,
                SchedulesRetention: dto.SchedulesRetention,
                OperatorAuditRetention: dto.OperatorAuditRetention).EnsureValid();
        }
        catch (ArgumentException ex)
        {
            throw new ConfigurationValidationException(
                $"Retention policy {filePath} violates domain invariants: {ex.Message}", ex);
        }
    }

    public Schedule LoadSchedule(string filePath)
    {
        var node = LoadAndValidate(filePath, _scheduleSchema);

        var dto = node.Deserialize<ScheduleDto>(_serializerOptions)
            ?? throw new ConfigurationValidationException($"Failed to deserialize {filePath} as schedule.");

        if (dto.Windows is null)
        {
            throw new ConfigurationValidationException($"{filePath} has no window list.");
        }

        var type = dto.Type switch
        {
            "day_ahead" => ScheduleType.DayAhead,
            "intraday" => ScheduleType.Intraday,
            "regel_leistung_reserve" => ScheduleType.RegelLeistungReserve,
            _ => throw new ConfigurationValidationException(
                $"{filePath} has unknown schedule type '{dto.Type}'."),
        };

        var windows = new List<ScheduleWindow>(dto.Windows.Count);
        foreach (var w in dto.Windows)
        {
            // Schema enforces the trailing 'Z' so the parsed DateTimeOffset is
            // already UTC-anchored. ToUniversalTime() is a defensive no-op
            // here; it keeps callers safe if the schema ever loosens.
            windows.Add(new ScheduleWindow(
                w.Start.ToUniversalTime(),
                w.End.ToUniversalTime(),
                w.TargetPowerKw));
        }

        try
        {
            return new Schedule(dto.AssetId, type, dto.MarketBidArea, dto.Version, windows);
        }
        catch (ArgumentException ex)
        {
            throw new ConfigurationValidationException(
                $"Schedule {filePath} violates domain invariants: {ex.Message}", ex);
        }
    }

    // Returns null when no LH-DOM-005 device-point fields are set, so the
    // adapter mapping doesn't carry a placeholder when the operator hasn't
    // filled the metadata. exportable=true is the schema default, so a
    // mapping with only that field set still counts as "no metadata" — we
    // surface DevicePoint only when at least one human-meaningful field
    // (display name, unit, alarm, value explanation, or an explicit
    // exportable=false) is present.
    private static DevicePointMetadata? BuildDevicePoint(
        string? displayName,
        string? unit,
        bool exportable,
        DevicePointAlarmDto? alarm,
        Dictionary<string, string>? valueExplanation)
    {
        var hasContent = !string.IsNullOrWhiteSpace(displayName)
            || !string.IsNullOrWhiteSpace(unit)
            || alarm is not null
            || valueExplanation is { Count: > 0 }
            || !exportable;
        if (!hasContent)
        {
            return null;
        }

        DevicePointAlarm? alarmRecord = alarm is null
            ? null
            : new DevicePointAlarm(alarm.Min, alarm.Max, alarm.Severity);
        IReadOnlyDictionary<string, string>? explanation = valueExplanation is { Count: > 0 }
            ? new Dictionary<string, string>(valueExplanation, StringComparer.Ordinal)
            : null;
        return new DevicePointMetadata(displayName, unit, exportable, alarmRecord, explanation);
    }

    private static Dictionary<int, string> ConvertEnum(
        Dictionary<string, string> source,
        string filePath,
        string registerName)
    {
        var result = new Dictionary<int, string>(source.Count);
        foreach (var (key, value) in source)
        {
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new ConfigurationValidationException(
                    $"{filePath} register '{registerName}' has non-integer enum key '{key}'.");
            }

            result[parsed] = value;
        }

        return result;
    }

    // Process-wide cache: JsonSchema.Net 8.0 auto-registers each
    // schema in SchemaRegistry.Global by its $id and rejects duplicate
    // registrations. Repeated FromFile calls on the same path would
    // otherwise throw on every test that constructs a second loader.
    // Lazy<T> with ExecutionAndPublication serialises the FromFile call
    // for any given path so two parallel test classes loading the same
    // schema directory don't race the registry. Different paths are
    // independent — the dictionary lookup is non-blocking.
    private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> _schemaCache =
        new(StringComparer.Ordinal);

    private static JsonSchema LoadSchema(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Schema not found: {path}", path);
        }

        return _schemaCache.GetOrAdd(
            path,
            p => new Lazy<JsonSchema>(
                () => JsonSchema.FromFile(p),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static JsonNode LoadAndValidate(string filePath, JsonSchema schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return ValidateNode(filePath, LoadJson(filePath), schema);
    }

    private static JsonNode LoadJson(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new ConfigurationValidationException($"Configuration file not found: {filePath}");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(filePath));
        }
        catch (JsonException ex)
        {
            throw new ConfigurationValidationException($"Configuration file is not valid JSON: {filePath}", ex);
        }

        if (node is null)
        {
            throw new ConfigurationValidationException($"Configuration file is empty: {filePath}");
        }

        return node;
    }

    private static JsonNode ValidateNode(string filePath, JsonNode node, JsonSchema schema)
    {
        // JsonSchema.Net 8.0 broke the JsonNode-based Evaluate signature
        // in favour of JsonElement (cuts allocations, matches the
        // System.Text.Json zero-copy idiom). Deserialize<JsonElement>
        // round-trips through the parser; cheaper than re-reading the
        // file and keeps the JsonNode parse-error path above intact.
        var element = node.Deserialize<JsonElement>();
        var results = schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!results.IsValid)
        {
            var messages = CollectErrors(results).ToList();
            throw new ConfigurationValidationException(
                $"Schema validation failed for {filePath}: {string.Join("; ", messages)}");
        }

        return node;
    }

    private static IEnumerable<string> CollectErrors(EvaluationResults results)
    {
        // JsonSchema.Net 8.0 removed HasErrors / HasDetails — both
        // collection properties are nullable on a node that didn't
        // evaluate or didn't fail, so a single null-check replaces
        // the old has-flag pattern.
        if (results.Errors is { Count: > 0 } errors)
        {
            foreach (var (keyword, message) in errors)
            {
                yield return $"at {results.InstanceLocation} ({keyword}): {message}";
            }
        }

        if (results.Details is { Count: > 0 } details)
        {
            foreach (var detail in details)
            {
                foreach (var nested in CollectErrors(detail))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string GetRequiredString(JsonNode node, string key)
    {
        var value = node[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationValidationException($"Missing required field '{key}'.");
        }

        return value;
    }

    private static double GetRequiredDouble(JsonNode node, string key)
    {
        var value = node[key]?.GetValue<double>();
        if (value is null)
        {
            throw new ConfigurationValidationException($"Missing required field '{key}'.");
        }

        return value.Value;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record ModbusMappingDto(
        string ProfileName,
        string UnitIdDiscovery,
        int? StaticUnitId,
        List<ModbusRegisterDto>? Registers,
        string? SchemaVersion = null);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record ModbusRegisterDto(
        string Name,
        int Address,
        string Type,
        double ScaleFactor,
        double[]? Range,
        bool Writable,
        string WriteCadence,
        string AuthRequired,
        Dictionary<string, string>? Enum,
        string? FirmwareConstraint,
        int? SunspecModel,
        // LH-DOM-005 device-point base (optional). Default-true for
        // Exportable matches the schema default.
        string? DisplayName = null,
        string? Unit = null,
        bool Exportable = true,
        DevicePointAlarmDto? Alarm = null,
        Dictionary<string, string>? ValueExplanation = null,
        // RM-M2-HIL-01: register-table + word-order. Both default to
        // the M1 values when absent from the JSON, so existing
        // profiles round-trip unchanged.
        string? RegisterTable = null,
        string? WordOrder = null);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record MqttMappingDto(
        string ProfileName,
        List<MqttTopicDto>? Topics,
        string? SchemaVersion = null);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record MqttTopicDto(
        string Name,
        string Topic,
        string Direction,
        string PayloadFormat,
        bool Retained,
        string AuthRequired,
        // LH-DOM-005 device-point base (optional).
        string? DisplayName = null,
        string? Unit = null,
        bool Exportable = true,
        DevicePointAlarmDto? Alarm = null,
        Dictionary<string, string>? ValueExplanation = null);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record OpcUaMappingDto(
        string SchemaVersion,
        string ProfileName,
        List<OpcUaNodeDto>? Nodes);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record OpcUaNodeDto(
        string Name,
        string NodeId,
        string Direction,
        string DataType,
        string AuthRequired,
        // Optional schema fields — defaults handled at the mapping
        // layer (ScaleFactor=1.0 if null, Writable=false if null).
        double? ScaleFactor = null,
        bool? Writable = null,
        string? WriteCadence = null,
        int? MonitoringIntervalMs = null,
        // LH-DOM-005 device-point base (optional).
        string? DisplayName = null,
        string? Unit = null,
        bool Exportable = true,
        DevicePointAlarmDto? Alarm = null,
        Dictionary<string, string>? ValueExplanation = null);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record DevicePointAlarmDto(
        double? Min,
        double? Max,
        string? Severity);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record ScheduleDto(
        string AssetId,
        string Type,
        string MarketBidArea,
        int Version,
        List<ScheduleWindowDto>? Windows);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record ScheduleWindowDto(
        DateTimeOffset Start,
        DateTimeOffset End,
        double TargetPowerKw);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record RetentionPolicyDto(
        TimeSpan? TelemetryRetention,
        TimeSpan? CommandsRetention,
        TimeSpan? SchedulesRetention,
        TimeSpan? OperatorAuditRetention);
}
