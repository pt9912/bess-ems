using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;
using Json.Schema.Generation;

namespace BatteryEms.Adapters.Mqtt.Tests;

// ADR 0013 §5.1: generates the field-normative MQTT payload envelope schema from the
// MqttPayloads records (C#->Schema). JsonSchema.Net.Generation supplies field names
// (it honors [JsonPropertyName]) + primitive types; two deterministic post-transforms
// close the gaps the generator leaves:
//  (1) `required` = the non-nullable properties. MqttJson.Options uses WhenWritingNull,
//      so only null-valued *nullable* fields drop from the wire; non-nullable fields are
//      always emitted -> required.
//  (2) DateTimeOffset override. The generator expands DateTimeOffset into the struct's
//      members (+ nested $defs); System.Text.Json serializes it as an ISO-8601 string,
//      so those properties are overridden to {type:string, format:date-time} and the
//      orphaned nested $defs dropped.
internal static class EnvelopeSchema
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static JsonSchema TelemetrySchema() => ToSchema<TelemetrySnapshotPayload>();

    public static JsonSchema CommandSchema() => ToSchema<CommandPayload>();

    public static JsonSchema CommandAckSchema() => ToSchema<CommandAckPayload>();

    // The committed config/schema/mqtt-telemetry-envelope.schema.json is exactly this
    // string; the drift check regenerates and compares it.
    public static string GenerateCanonicalJson()
    {
        var envelope = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://bess-ems.io/schema/mqtt-telemetry-envelope.json",
            ["title"] = "MQTT payload envelope (ADR 0013 5.1)",
            ["description"] = "Generated from BatteryEms.Adapters.Mqtt.MqttPayloads (C# to Schema). Field-normative wire contract for the telemetry/command/command_ack payloads. Do not hand-edit; regenerate.",
            ["$defs"] = new JsonObject
            {
                ["telemetry"] = BuildNode<TelemetrySnapshotPayload>(),
                ["command"] = BuildNode<CommandPayload>(),
                ["command_ack"] = BuildNode<CommandAckPayload>(),
            },
        };
        return envelope.ToJsonString(Pretty);
    }

    private static JsonSchema ToSchema<T>() =>
        JsonSerializer.Deserialize<JsonSchema>(BuildNode<T>())
        ?? throw new InvalidOperationException("envelope schema deserialized to null");

    private static JsonObject BuildNode<T>()
    {
        var generated = new JsonSchemaBuilder()
            .FromType<T>()
            .Required(RequiredWireFields(typeof(T)))
            .Build();
        var node = JsonSerializer.SerializeToNode(generated)?.AsObject()
            ?? throw new InvalidOperationException("generated schema serialized to null");
        OverrideDateTimeOffsets<T>(node);
        return node;
    }

    private static void OverrideDateTimeOffsets<T>(JsonObject node)
    {
        if (node["properties"]?.AsObject() is not { } props)
        {
            return;
        }

        var overrode = false;
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType == typeof(DateTimeOffset))
            {
                props[JsonName(prop)] = new JsonObject { ["type"] = "string", ["format"] = "date-time" };
                overrode = true;
            }
        }

        if (overrode)
        {
            node.Remove("$defs");
        }
    }

    private static string[] RequiredWireFields(Type type)
    {
        var ctx = new NullabilityInfoContext();
        var required = new List<string>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var valueTypeNullable = Nullable.GetUnderlyingType(prop.PropertyType) is not null;
            var refTypeNullable = ctx.Create(prop).ReadState == NullabilityState.Nullable;
            if (!valueTypeNullable && !refTypeNullable)
            {
                required.Add(JsonName(prop));
            }
        }

        return [.. required];
    }

    private static string JsonName(PropertyInfo prop) =>
        prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;
}
