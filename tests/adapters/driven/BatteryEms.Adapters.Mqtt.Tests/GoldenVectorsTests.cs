using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BatteryEms.Domain;
using Json.Schema;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

// ADR 0013 §5.2 sub-slice 3: two directions on the golden-vector suite.
//  (a) ems-authority drift gate: the committed command vectors must equal the
//      freshly lifted ones, so a CommandPayload/serializer change without a
//      vector refresh fails here (mirror of the Go field-vectors gate).
//  (b) field-vector consumption — the §5.1 coverage gap: the committed FIELD
//      vectors (lifted from serializer.go) are for the first time validated
//      against the envelope schema (plus exact key-set equality: the schema
//      has no additionalProperties:false, so validation alone cannot catch an
//      ADDED producer field), decoded through the real MqttTelemetrySource
//      path, and correlated end-to-end: the ems-manifest nominal command is
//      dispatched through MqttCommandSink and must correlate with the
//      field-manifest command_ack (the echo invariant, gate-checked instead
//      of a cross-file convention).
public sealed class GoldenVectorsTests
{
    [Fact]
    public void Committed_ems_manifest_matches_generator_no_drift()
    {
        var committed = JsonNode.Parse(File.ReadAllText(GoldenVectors.EmsManifestPath));
        var regenerated = JsonNode.Parse(GoldenVectors.GenerateEmsManifestJson());

        Assert.True(
            JsonNode.DeepEquals(committed, regenerated),
            "config/schema/vectors/mqtt-golden-vectors.ems.v1.json is out of sync with the C# wire types; "
            + "replace its content with:\n" + GoldenVectors.GenerateEmsManifestJson());
    }

    [Fact]
    public void Field_telemetry_payloads_validate_against_envelope_schema_with_exact_key_set()
    {
        var telemetryCases = FieldCases("telemetry");
        Assert.NotEmpty(telemetryCases);

        var schemaKeys = EnvelopeTelemetryPropertyNames();
        foreach (var vectorCase in telemetryCases)
        {
            var payload = vectorCase["payload"]!.AsObject();
            using var doc = JsonDocument.Parse(payload.ToJsonString());
            var results = EnvelopeSchema.TelemetrySchema().Evaluate(doc.RootElement);
            Assert.True(results.IsValid, $"case {vectorCase["name"]}: field-producer payload does not validate against the envelope schema");

            var payloadKeys = payload.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.Equal(schemaKeys, payloadKeys);
        }
    }

    [Fact]
    public async Task Field_telemetry_payloads_decode_through_the_telemetry_source()
    {
        foreach (var vectorCase in FieldCases("telemetry"))
        {
            var payload = vectorCase["payload"]!.AsObject();
            var client = new FakeMqttClient();
            var source = new MqttTelemetrySource(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var consumer = ReadFirst(source, cts.Token);

            await TestHelpers.WaitUntil(() => client.SubscribedTopics.Count > 0, TimeSpan.FromSeconds(1));
            await client.DeliverAsync(vectorCase["topic"]!.GetValue<string>(), Encoding.UTF8.GetBytes(payload.ToJsonString()));

            var telemetry = await consumer;
            Assert.Equal(payload["soc_percent"]!.GetValue<double>(), telemetry.SocPercent);
            Assert.Equal(payload["soh_percent"]!.GetValue<double>(), telemetry.SohPercent);
            Assert.Equal(payload["active_power_kw"]!.GetValue<double>(), telemetry.ActivePowerKw);
            Assert.Equal(payload["reactive_power_kvar"]!.GetValue<double>(), telemetry.ReactivePowerKvar);
            Assert.Equal(payload["dc_voltage"]!.GetValue<double>(), telemetry.DcVoltage);
            Assert.Equal(payload["dc_current"]!.GetValue<double>(), telemetry.DcCurrent);
            Assert.Equal(payload["temperature_celsius"]!.GetValue<double>(), telemetry.TemperatureCelsius);
            Assert.Equal(payload["available"]!.GetValue<bool>(), telemetry.Available);
            Assert.Equal(payload["fault_status"]!.GetValue<string>(), telemetry.FaultStatus);
        }
    }

    [Fact]
    public async Task Echo_roundtrip_dispatches_ems_nominal_command_and_correlates_field_ack()
    {
        // Data-driven from the COMMITTED manifests: the command comes from the
        // ems file, the ack from the field file. If their command_ids diverge,
        // this correlation fails — the echo invariant is a gate, not a comment.
        var commandPayload = EmsCase("command-nominal")["payload"]!.AsObject();
        var command = new BatteryCommand(
            CommandId: commandPayload["command_id"]!.GetValue<string>(),
            Timestamp: commandPayload["timestamp"]!.GetValue<DateTimeOffset>(),
            AssetId: commandPayload["asset_id"]!.GetValue<string>(),
            Mode: Enum.Parse<CommandMode>(commandPayload["mode"]!.GetValue<string>()),
            ActivePowerKw: commandPayload["active_power_kw"]!.GetValue<double>(),
            ReactivePowerKvar: commandPayload["reactive_power_kvar"]?.GetValue<double>(),
            ValidUntil: commandPayload["valid_until"]!.GetValue<DateTimeOffset>(),
            Reason: commandPayload["reason"]!.GetValue<string>(),
            Source: Enum.Parse<CommandSource>(commandPayload["source"]!.GetValue<string>()));

        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), MqttFixtures.SampleAsset(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());
        var write = sink.WriteAsync(command, CancellationToken.None);

        await TestHelpers.WaitUntil(
            () => client.Publishes.Count > 0 && client.SubscribedTopicNames.Contains("battery/asset-1/command/ack", StringComparer.Ordinal),
            TimeSpan.FromSeconds(1));

        // The real sink output must structurally equal the manifest payload
        // (values sit inside the SampleAsset limits, so no clamping applies).
        var published = JsonNode.Parse(client.Publishes[0].Payload);
        Assert.True(
            JsonNode.DeepEquals(published, commandPayload),
            $"MqttCommandSink wire payload drifted from the ems manifest:\n{published?.ToJsonString()}");

        var ack = FieldCase("command-ack-accepted-echo")["payload"]!;
        await client.DeliverAsync("battery/asset-1/command/ack", Encoding.UTF8.GetBytes(ack.ToJsonString()));

        var result = await write;
        Assert.True(result.Success, "field-manifest ack did not correlate with the ems-manifest command — echo invariant broken");
        Assert.Equal("accepted", result.Reason);
    }

    [Fact]
    public void Field_ack_payload_decodes_via_CommandAckPayload()
    {
        var ack = FieldCase("command-ack-accepted-echo")["payload"]!;
        var decoded = JsonSerializer.Deserialize<CommandAckPayload>(ack.ToJsonString(), MqttJson.Options);

        Assert.NotNull(decoded);
        Assert.Equal(GoldenVectors.NominalCommandId, decoded!.CommandId);
        Assert.True(decoded.Accepted);
        Assert.Equal("accepted", decoded.Reason);
    }

    private static JsonObject FieldManifest() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(GoldenVectors.FieldManifestPath))!;

    private static JsonObject EmsManifest() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(GoldenVectors.EmsManifestPath))!;

    private static List<JsonObject> FieldCases(string topicName) =>
        FieldManifest()["cases"]!.AsArray()
            .Select(c => c!.AsObject())
            .Where(c => c["topic_name"]!.GetValue<string>() == topicName && c["payload"] is not null)
            .ToList();

    private static JsonObject FieldCase(string name) =>
        FieldManifest()["cases"]!.AsArray()
            .Select(c => c!.AsObject())
            .Single(c => c["name"]!.GetValue<string>() == name);

    private static JsonObject EmsCase(string name) =>
        EmsManifest()["cases"]!.AsArray()
            .Select(c => c!.AsObject())
            .Single(c => c["name"]!.GetValue<string>() == name);

    // Exact key set of the COMMITTED envelope schema's telemetry definition —
    // the published consumer expectation the field producer must match.
    private static string[] EnvelopeTelemetryPropertyNames()
    {
        var schema = JsonNode.Parse(File.ReadAllText(GoldenVectors.EnvelopeSchemaPath))!;
        return schema["$defs"]!["telemetry"]!["properties"]!.AsObject()
            .Select(p => p.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
    }

    private static Task<BatteryTelemetry> ReadFirst(MqttTelemetrySource source, CancellationToken ct) => Task.Run(async () =>
    {
        await foreach (var t in source.ReadAsync(ct))
        {
            return t;
        }

        throw new InvalidOperationException("no telemetry produced");
    }, ct);
}
