using System.Text.Json;
using System.Text.Json.Nodes;

namespace BatteryEms.Adapters.Mqtt.Tests;

// ADR 0013 §5.2: ems-authority golden vectors. The command payloads are
// LIFTED from the real serializer (MqttJson.Options over the CommandPayload
// wire type) — never hand-listed — and the topic/retained facts come from the
// shipped example mapping, mirroring how the Go generator lifts the field
// side. Values stay inside the SampleAsset power limits so the command sink
// publishes them unclamped and the wire payload equals the manifest payload.
internal static class GoldenVectors
{
    public const string AssetId = "asset-1";

    // Echoed by the command_ack case of the FIELD manifest; the correlation
    // harness in GoldenVectorsTests gate-checks the echo invariant.
    public const string NominalCommandId = "cmd-golden-nominal";
    public const string NoReactiveCommandId = "cmd-golden-no-reactive";

    public static string VectorsDir => Path.Combine(RepoRoot(), "config", "schema", "vectors");

    public static string EmsManifestPath => Path.Combine(VectorsDir, "mqtt-golden-vectors.ems.v1.json");

    public static string FieldManifestPath => Path.Combine(VectorsDir, "mqtt-golden-vectors.field.v1.json");

    public static string EnvelopeSchemaPath => Path.Combine(RepoRoot(), "config", "schema", "mqtt-telemetry-envelope.schema.json");

    public static CommandPayload NominalCommand() => new(
        CommandId: NominalCommandId,
        Timestamp: DateTimeOffset.UnixEpoch,
        AssetId: AssetId,
        Mode: "Charge",
        ActivePowerKw: -25.5,
        ReactivePowerKvar: 12.5,
        ValidUntil: DateTimeOffset.UnixEpoch.AddMinutes(1),
        Reason: "schedule-dispatch",
        Source: "Schedule");

    public static CommandPayload NoReactiveCommand() => new(
        CommandId: NoReactiveCommandId,
        Timestamp: DateTimeOffset.UnixEpoch,
        AssetId: AssetId,
        Mode: "Discharge",
        ActivePowerKw: 25,
        ReactivePowerKvar: null,
        ValidUntil: DateTimeOffset.UnixEpoch.AddMinutes(1),
        Reason: "operator-dispatch",
        Source: "Operator");

    public static string GenerateEmsManifestJson()
    {
        var (topic, retained) = CommandTopicFromExampleMapping();
        var manifest = new JsonObject
        {
            ["schema_version"] = "golden-vector-manifest.v1",
            ["contract"] = "mqtt",
            ["authority"] = "ems",
            ["cases"] = new JsonArray(
                EmsCase(
                    "command-nominal", topic, retained,
                    "Full command as the EMS dispatches it; command_id is echoed by the command_ack case of the field manifest.",
                    NominalCommand()),
                EmsCase(
                    "command-no-reactive-power", topic, retained,
                    "reactive_power_kvar is null and WhenWritingNull drops the member entirely: the field must not appear on the wire.",
                    NoReactiveCommand())),
        };
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject EmsCase(string name, string topic, bool retained, string description, CommandPayload payload) => new()
    {
        ["name"] = name,
        ["topic_name"] = "command",
        ["direction"] = "publish",
        ["topic"] = topic,
        ["retained"] = retained,
        ["description"] = description,
        ["payload"] = JsonNode.Parse(JsonSerializer.Serialize(payload, MqttJson.Options)),
    };

    private static (string Topic, bool Retained) CommandTopicFromExampleMapping()
    {
        var path = Path.Combine(RepoRoot(), "config", "examples", "adapters", "mqtt.simulator.json");
        var mapping = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"mapping did not parse: {path}");
        foreach (var entry in mapping["topics"]!.AsArray())
        {
            if (entry!["name"]!.GetValue<string>() == "command" && entry["direction"]!.GetValue<string>() == "publish")
            {
                var template = entry["topic"]!.GetValue<string>();
                return (template.Replace("{assetId}", AssetId, StringComparison.Ordinal), entry["retained"]!.GetValue<bool>());
            }
        }

        throw new InvalidOperationException($"no EMS-publish command topic in {path}");
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
