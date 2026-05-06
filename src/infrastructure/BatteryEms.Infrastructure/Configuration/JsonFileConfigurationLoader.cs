using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BatteryEms.Application.Configuration;
using BatteryEms.Domain;
using Json.Schema;

namespace BatteryEms.Infrastructure.Configuration;

public sealed class JsonFileConfigurationLoader : IConfigurationLoader
{
    private readonly JsonSchema _assetSchema;
    private readonly JsonSchema _modbusSchema;
    private readonly JsonSchema _mqttSchema;
    private readonly JsonSchema _scheduleSchema;
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonFileConfigurationLoader(string schemaDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDirectory);
        if (!Directory.Exists(schemaDirectory))
        {
            throw new DirectoryNotFoundException($"Schema directory not found: {schemaDirectory}");
        }

        _assetSchema = LoadSchema(schemaDirectory, "asset.schema.json");
        _modbusSchema = LoadSchema(schemaDirectory, "modbus-mapping.schema.json");
        _mqttSchema = LoadSchema(schemaDirectory, "mqtt-mapping.schema.json");
        _scheduleSchema = LoadSchema(schemaDirectory, "schedule.schema.json");

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
                SunspecModel: r.SunspecModel));
        }

        return new ModbusMappingConfiguration(
            ProfileName: dto.ProfileName,
            UnitIdDiscovery: dto.UnitIdDiscovery,
            StaticUnitId: dto.StaticUnitId,
            Registers: registers);
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
            .Select(t => new MqttTopicMapping(t.Name, t.Topic, t.Direction, t.PayloadFormat, t.Retained, t.AuthRequired))
            .ToList();

        return new MqttMappingConfiguration(dto.ProfileName, topics);
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

    private static JsonSchema LoadSchema(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Schema not found: {path}", path);
        }

        return JsonSchema.FromFile(path);
    }

    private static JsonNode LoadAndValidate(string filePath, JsonSchema schema)
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

        var results = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
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
        if (results.HasErrors && results.Errors is { Count: > 0 } errors)
        {
            foreach (var (keyword, message) in errors)
            {
                yield return $"at {results.InstanceLocation} ({keyword}): {message}";
            }
        }

        foreach (var detail in results.Details)
        {
            foreach (var nested in CollectErrors(detail))
            {
                yield return nested;
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record ModbusMappingDto(
        string ProfileName,
        string UnitIdDiscovery,
        int? StaticUnitId,
        List<ModbusRegisterDto>? Registers);

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
        int? SunspecModel);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record MqttMappingDto(
        string ProfileName,
        List<MqttTopicDto>? Topics);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record MqttTopicDto(
        string Name,
        string Topic,
        string Direction,
        string PayloadFormat,
        bool Retained,
        string AuthRequired);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record ScheduleDto(
        string AssetId,
        string Type,
        string MarketBidArea,
        int Version,
        List<ScheduleWindowDto>? Windows);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer via reflection.")]
    private sealed record ScheduleWindowDto(
        DateTimeOffset Start,
        DateTimeOffset End,
        double TargetPowerKw);
}
