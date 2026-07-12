using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

// ADR 0013 §5.1 sub-slice 2: two-sided check on the MQTT payload envelope schema.
//  (1) Drift: the committed schema must equal the freshly generated one, so a change to
//      MqttPayloads.cs without regenerating fails here.
//  (2) Serializer round-trip: real DTO instances serialized with MqttJson.Options must
//      validate against the schema — this anchors the schema to the actual System.Text.Json
//      wire output (naming + required + null-omission), not just the generator's view.
public sealed class EnvelopeSchemaTests
{
    private static string CommittedSchemaPath =>
        Path.Combine(RepoRoot(), "config", "schema", "mqtt-telemetry-envelope.schema.json");

    [Fact]
    public void Committed_schema_matches_generator_no_drift()
    {
        var committed = JsonNode.Parse(File.ReadAllText(CommittedSchemaPath));
        var regenerated = JsonNode.Parse(EnvelopeSchema.GenerateCanonicalJson());

        Assert.True(
            JsonNode.DeepEquals(committed, regenerated),
            "config/schema/mqtt-telemetry-envelope.schema.json is out of sync with MqttPayloads.cs; regenerate it.");
    }

    [Fact]
    public void Telemetry_serializer_output_validates()
    {
        var payload = new TelemetrySnapshotPayload(0, 60.5, 99, 0, 0, 800, 0, 22, true, "ok");
        AssertValid(EnvelopeSchema.TelemetrySchema(), payload);
    }

    [Fact]
    public void Command_validates_with_and_without_reactive_power()
    {
        var full = new CommandPayload("c1", DateTimeOffset.UnixEpoch, "a1", "Stop", 0, 0, DateTimeOffset.UnixEpoch, "r", "Fallback");
        AssertValid(EnvelopeSchema.CommandSchema(), full);
        // WhenWritingNull drops reactive_power_kvar; the schema must not require it.
        AssertValid(EnvelopeSchema.CommandSchema(), full with { ReactivePowerKvar = null });
    }

    [Fact]
    public void CommandAck_validates_with_and_without_reason()
    {
        var full = new CommandAckPayload("c1", true, DateTimeOffset.UnixEpoch, "accepted");
        AssertValid(EnvelopeSchema.CommandAckSchema(), full);
        AssertValid(EnvelopeSchema.CommandAckSchema(), full with { Reason = null });
    }

    private static void AssertValid<T>(JsonSchema schema, T payload)
    {
        var json = JsonSerializer.Serialize(payload, MqttJson.Options);
        using var doc = JsonDocument.Parse(json);
        var results = schema.Evaluate(doc.RootElement);
        Assert.True(results.IsValid, $"serializer output did not validate against the envelope schema: {json}");
    }

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
}
