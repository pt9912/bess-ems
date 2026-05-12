using System.Text.Json;
using BatteryEms.Domain;

namespace BatteryEms.Application.Tests.Replay;

internal static class TelemetryReplayJsonLoader
{
    private static readonly HashSet<string> FixtureFields = new(StringComparer.Ordinal)
    {
        "schema_version",
        "records",
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

    public static IReadOnlyList<TelemetryReplayRecord> LoadFixture(string fixturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);

        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = new ReplayJsonReader(document.RootElement, "$");
        root.RejectUnknownProperties(FixtureFields);
        RequireSchemaVersion(root, ReplaySchemaVersions.TelemetryFixture);
        return root.RequiredArray("records", ReadRecord);
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
