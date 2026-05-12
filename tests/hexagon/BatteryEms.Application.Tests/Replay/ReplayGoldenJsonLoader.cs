using System.Text.Json;
using BatteryEms.Domain;

namespace BatteryEms.Application.Tests.Replay;

internal static class ReplayGoldenJsonLoader
{
    private static readonly HashSet<string> GoldenFields = new(StringComparer.Ordinal)
    {
        "schema_version",
        "commands",
    };

    private static readonly HashSet<string> CommandFields = new(StringComparer.Ordinal)
    {
        "command_id",
        "timestamp_utc",
        "asset_id",
        "mode",
        "active_power_kw",
        "reactive_power_kvar",
        "reason",
        "source",
    };

    public static IReadOnlyList<ReplayGoldenCommand> Load(string goldenPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goldenPath);

        using var document = JsonDocument.Parse(File.ReadAllText(goldenPath));
        var root = new ReplayJsonReader(document.RootElement, "$");
        root.RejectUnknownProperties(GoldenFields);
        RequireSchemaVersion(root);
        return root.RequiredArray("commands", ReadCommand);
    }

    private static ReplayGoldenCommand ReadCommand(JsonElement item, string path)
    {
        var reader = new ReplayJsonReader(item, path);
        reader.RejectUnknownProperties(CommandFields);
        var rawMode = reader.RequiredString("mode");
        if (!Enum.TryParse<CommandMode>(rawMode, ignoreCase: false, out var mode))
        {
            throw new ReplayJsonException("invalid_enum", $"{reader.Path}.mode", $"Unknown command mode '{rawMode}'.");
        }

        var rawSource = reader.RequiredString("source");
        if (!Enum.TryParse<CommandSource>(rawSource, ignoreCase: false, out var source))
        {
            throw new ReplayJsonException("invalid_enum", $"{reader.Path}.source", $"Unknown command source '{rawSource}'.");
        }

        return new ReplayGoldenCommand(
            CommandId: reader.RequiredString("command_id"),
            Timestamp: reader.RequiredDateTimeOffset("timestamp_utc"),
            AssetId: reader.RequiredString("asset_id"),
            Mode: mode,
            ActivePowerKw: reader.RequiredFiniteDouble("active_power_kw"),
            ReactivePowerKvar: reader.RequiredFiniteDouble("reactive_power_kvar"),
            Reason: reader.RequiredString("reason"),
            Source: source);
    }

    private static void RequireSchemaVersion(ReplayJsonReader reader)
    {
        var actual = reader.RequiredString("schema_version");
        if (!StringComparer.Ordinal.Equals(actual, ReplaySchemaVersions.GoldenCommands))
        {
            throw new ReplayJsonException(
                "unsupported_schema_version",
                "$.schema_version",
                $"Unsupported golden schema '{actual}'.");
        }
    }
}
