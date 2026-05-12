using System.Text.Json;
using BatteryEms.Application.Markets;
using BatteryEms.Domain;

namespace BatteryEms.Application.Tests.Replay;

internal static class TelemetryReplayJsonLoader
{
    private static readonly HashSet<string> FixtureFields = new(StringComparer.Ordinal)
    {
        "schema_version",
        "records",
        "schedules",
    };

    private static readonly HashSet<string> RecordFields = new(StringComparer.Ordinal)
    {
        "timestamp_utc",
        "received_at_utc",
        "telemetry",
    };

    private static readonly HashSet<string> TelemetryFields = new(StringComparer.Ordinal)
    {
        "timestamp_utc",
        "asset_id",
        "soc_percent",
        "soh_percent",
        "active_power_kw",
        "reactive_power_kvar",
        "dc_voltage",
        "dc_current",
        "temperature_celsius",
        "available",
        "fault_status",
        "data_quality",
    };

    private static readonly HashSet<string> DataQualityFields = new(StringComparer.Ordinal)
    {
        "flag",
        "reason",
    };

    private static readonly HashSet<string> ScheduleFields = new(StringComparer.Ordinal)
    {
        "asset_id",
        "type",
        "market_bid_area",
        "version",
        "windows",
    };

    private static readonly HashSet<string> ScheduleWindowFields = new(StringComparer.Ordinal)
    {
        "start_utc",
        "end_utc",
        "target_power_kw",
    };

    public static IReadOnlyList<TelemetryReplayRecord> LoadFixture(string fixturePath)
        => LoadDataset(fixturePath).Records;

    public static TelemetryReplayDataset LoadDataset(string fixturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);

        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = new ReplayJsonReader(document.RootElement, "$");
        root.RejectUnknownProperties(FixtureFields);
        RequireSchemaVersion(root, ReplaySchemaVersions.TelemetryFixture);
        return new TelemetryReplayDataset(
            Records: root.RequiredArray("records", ReadRecord),
            Schedules: ReadSchedules(root));
    }

    private static TelemetryReplayRecord ReadRecord(JsonElement item, string path)
    {
        var reader = new ReplayJsonReader(item, path);
        reader.RejectUnknownProperties(RecordFields);

        var telemetryReader = reader.OptionalObject("telemetry");
        var telemetry = telemetryReader is null ? null : ReadTelemetry(telemetryReader);
        return new TelemetryReplayRecord(
            Timestamp: reader.RequiredDateTimeOffset("timestamp_utc"),
            Telemetry: telemetry,
            ReceivedAt: reader.OptionalDateTimeOffset("received_at_utc"));
    }

    private static BatteryTelemetry ReadTelemetry(ReplayJsonReader reader)
    {
        reader.RejectUnknownProperties(TelemetryFields);
        return new BatteryTelemetry(
            Timestamp: reader.RequiredDateTimeOffset("timestamp_utc"),
            AssetId: reader.RequiredString("asset_id"),
            SocPercent: reader.RequiredFiniteDouble("soc_percent"),
            SohPercent: reader.RequiredFiniteDouble("soh_percent"),
            ActivePowerKw: reader.RequiredFiniteDouble("active_power_kw"),
            ReactivePowerKvar: reader.RequiredFiniteDouble("reactive_power_kvar"),
            DcVoltage: reader.RequiredFiniteDouble("dc_voltage"),
            DcCurrent: reader.RequiredFiniteDouble("dc_current"),
            TemperatureCelsius: reader.RequiredFiniteDouble("temperature_celsius"),
            Available: reader.RequiredBoolean("available"),
            FaultStatus: reader.RequiredString("fault_status"),
            DataQuality: ReadDataQuality(reader.RequiredObject("data_quality")));
    }

    private static DataQuality ReadDataQuality(ReplayJsonReader reader)
    {
        reader.RejectUnknownProperties(DataQualityFields);
        var reason = reader.RequiredString("reason");
        var rawFlag = reader.RequiredString("flag");
        if (!Enum.TryParse<DataQualityState>(rawFlag, ignoreCase: false, out var flag))
        {
            throw new ReplayJsonException(
                "invalid_enum",
                $"{reader.Path}.flag",
                $"Unknown data quality '{rawFlag}'.");
        }

        return flag switch
        {
            DataQualityState.Valid => DataQuality.Valid,
            DataQualityState.Stale => DataQuality.Stale(reason),
            DataQualityState.Substituted => DataQuality.Substituted(reason),
            DataQualityState.ProtocolError => DataQuality.ProtocolError(reason),
            _ => throw new ReplayJsonException("invalid_enum", $"{reader.Path}.flag", "Unknown data quality."),
        };
    }

    private static IReadOnlyList<Schedule> ReadSchedules(ReplayJsonReader root)
    {
        if (root.OptionalArray("schedules") is not { } schedules)
        {
            return Array.Empty<Schedule>();
        }

        return schedules.Select(ReadSchedule).ToArray();
    }

    private static Schedule ReadSchedule((JsonElement Item, string Path) item)
    {
        var reader = new ReplayJsonReader(item.Item, item.Path);
        reader.RejectUnknownProperties(ScheduleFields);
        var rawType = reader.RequiredString("type");
        if (!Enum.TryParse<ScheduleType>(rawType, ignoreCase: false, out var type))
        {
            throw new ReplayJsonException("invalid_enum", $"{reader.Path}.type", $"Unknown schedule type '{rawType}'.");
        }

        return new Schedule(
            assetId: reader.RequiredString("asset_id"),
            type: type,
            marketBidArea: reader.RequiredString("market_bid_area"),
            version: reader.RequiredInt32("version"),
            windows: reader.RequiredArray("windows", ReadScheduleWindow));
    }

    private static ScheduleWindow ReadScheduleWindow(JsonElement item, string path)
    {
        var reader = new ReplayJsonReader(item, path);
        reader.RejectUnknownProperties(ScheduleWindowFields);
        return new ScheduleWindow(
            reader.RequiredDateTimeOffset("start_utc"),
            reader.RequiredDateTimeOffset("end_utc"),
            reader.RequiredFiniteDouble("target_power_kw"));
    }

    private static void RequireSchemaVersion(ReplayJsonReader reader, string expected)
    {
        var actual = reader.RequiredString("schema_version");
        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            throw new ReplayJsonException(
                "unsupported_schema_version",
                "$.schema_version",
                $"Unsupported fixture schema '{actual}'.");
        }
    }
}
