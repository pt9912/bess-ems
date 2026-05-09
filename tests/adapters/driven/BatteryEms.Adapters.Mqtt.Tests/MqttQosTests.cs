using BatteryEms.Adapters.Mqtt;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

// RM-M4-06: per-publisher / per-subscriber QoS configuration plus
// pinned correlation behaviour. Defaults follow plan-RM-M4 RM-M4-06
// design decision D-03; the wiring tests pin that the sink and the
// telemetry source pull the right QoS value from MqttAdapterOptions.QoS.
public sealed class MqttQosTests
{
    [Fact]
    public void MqttQosOptions_defaults_match_design_decision_D_03()
    {
        var qos = MqttQosOptions.Defaults;

        Assert.Equal(MqttQualityOfService.AtLeastOnce, qos.CommandPublish);
        Assert.Equal(MqttQualityOfService.AtLeastOnce, qos.CommandAckSubscribe);
        Assert.Equal(MqttQualityOfService.AtMostOnce, qos.TelemetrySubscribe);
    }

    [Fact]
    public void MqttQosOptions_wire_values_match_mqtt_spec_integers()
    {
        // Spec-mandated wire values — a config file's integer 0/1/2
        // must deserialise into the right enum case.
        Assert.Equal(0, (int)MqttQualityOfService.AtMostOnce);
        Assert.Equal(1, (int)MqttQualityOfService.AtLeastOnce);
        Assert.Equal(2, (int)MqttQualityOfService.ExactlyOnce);
    }

    [Fact]
    public void MqttAdapterOptions_QoSOrDefault_falls_back_to_defaults_when_null()
    {
        var options = new MqttAdapterOptions(
            BrokerHost: "h", BrokerPort: 1883, ClientId: "c", AssetId: "a-1",
            ConnectTimeout: TimeSpan.FromSeconds(1),
            CommandAckTimeout: TimeSpan.FromMilliseconds(200),
            QoS: null);

        Assert.NotNull(options.QoSOrDefault);
        Assert.Equal(MqttQualityOfService.AtLeastOnce, options.QoSOrDefault.CommandPublish);
    }

    [Fact]
    public async Task CommandSink_publishes_with_CommandPublish_qos()
    {
        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(
            client, MqttFixtures.SimulatorMapping(), MqttFixtures.SampleAsset(),
            MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var write = sink.WriteAsync(SampleCommand("cmd-pub-qos"), CancellationToken.None);
        await TestHelpers.WaitUntil(
            () => client.Publishes.Count > 0,
            TimeSpan.FromSeconds(1));

        Assert.Equal(MqttQualityOfService.AtLeastOnce, client.Publishes[0].Qos);

        // Drain the timeout so the WriteAsync task completes cleanly.
        await write;
    }

    [Fact]
    public async Task CommandSink_subscribes_to_ack_topic_with_CommandAckSubscribe_qos()
    {
        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(
            client, MqttFixtures.SimulatorMapping(), MqttFixtures.SampleAsset(),
            MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var write = sink.WriteAsync(SampleCommand("cmd-sub-qos"), CancellationToken.None);
        await TestHelpers.WaitUntil(
            () => client.SubscribedTopics.Count > 0,
            TimeSpan.FromSeconds(1));

        var ack = Assert.Single(client.SubscribedTopics);
        Assert.Equal("battery/asset-1/command/ack", ack.Topic);
        Assert.Equal(MqttQualityOfService.AtLeastOnce, ack.Qos);

        await write;
    }

    [Fact]
    public async Task TelemetrySource_subscribes_with_TelemetrySubscribe_qos()
    {
        var client = new FakeMqttClient();
        var source = new MqttTelemetrySource(
            client, MqttFixtures.SimulatorMapping(),
            MqttFixtures.Defaults("single-bess-1"), new MqttFixtures.FixedClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var consumer = ReadFirstOrCancel(source, cts.Token);

        await TestHelpers.WaitUntil(() => client.SubscribedTopics.Count > 0, TimeSpan.FromSeconds(1));

        var sub = Assert.Single(client.SubscribedTopics);
        Assert.Equal("battery/single-bess-1/telemetry", sub.Topic);
        Assert.Equal(MqttQualityOfService.AtMostOnce, sub.Qos);

        await cts.CancelAsync();
        await consumer;
    }

    [Fact]
    public async Task Custom_qos_overrides_default()
    {
        // Operator opts AtMostOnce for the command-publish channel
        // (low-overhead fire-and-forget) — the sink must pick that up.
        var customQos = new MqttQosOptions(
            CommandPublish: MqttQualityOfService.AtMostOnce);
        var optionsWithCustomQos = MqttFixtures.Defaults() with { QoS = customQos };

        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(
            client, MqttFixtures.SimulatorMapping(), MqttFixtures.SampleAsset(),
            optionsWithCustomQos, new MqttFixtures.FixedClock());

        var write = sink.WriteAsync(SampleCommand("cmd-custom"), CancellationToken.None);
        await TestHelpers.WaitUntil(
            () => client.Publishes.Count > 0,
            TimeSpan.FromSeconds(1));

        Assert.Equal(MqttQualityOfService.AtMostOnce, client.Publishes[0].Qos);

        await write;
    }

    // Mismatch behaviour (foreign-CommandId ACK silently dropped →
    // pending command runs into ack-timeout) is already pinned by
    // `MqttCommandSinkTests.WriteAsync_ignores_acks_for_unrelated_command_ids`
    // — no separate pin needed in the QoS suite.

    [Fact]
    public async Task Multiple_pending_commands_correlate_by_CommandId_not_by_insertion_order()
    {
        // Two commands published in parallel; ACKs delivered with
        // distinct Reason payloads. Pin asserts the Reason string lands
        // on the correct CommandDispatchResult, which catches the
        // hypothetical regression where the dispatcher resolves by
        // insertion order (e.g. `_pending.Values.First()`) instead of
        // by CommandId — that bug would still flip both Success=true
        // but would swap the Reason strings.
        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(
            client, MqttFixtures.SimulatorMapping(), MqttFixtures.SampleAsset(),
            MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var writeA = sink.WriteAsync(SampleCommand("cmd-A"), CancellationToken.None);
        var writeB = sink.WriteAsync(SampleCommand("cmd-B"), CancellationToken.None);

        await TestHelpers.WaitUntil(
            () => client.Publishes.Count >= 2,
            TimeSpan.FromSeconds(1));

        // Deliver the ACKs in REVERSE publication order, each with a
        // distinct Reason. The dispatcher must route each ACK to the
        // TCS keyed by its own CommandId, regardless of the order the
        // commands were published.
        await client.DeliverAsync("battery/asset-1/command/ack",
            System.Text.Encoding.UTF8.GetBytes("""{"command_id":"cmd-B","accepted":true,"reason":"reason-for-B"}"""));
        await client.DeliverAsync("battery/asset-1/command/ack",
            System.Text.Encoding.UTF8.GetBytes("""{"command_id":"cmd-A","accepted":true,"reason":"reason-for-A"}"""));

        var resultA = await writeA;
        var resultB = await writeB;
        Assert.True(resultA.Success);
        Assert.True(resultB.Success);
        Assert.Contains("reason-for-A", resultA.Reason, StringComparison.Ordinal);
        Assert.Contains("reason-for-B", resultB.Reason, StringComparison.Ordinal);
    }

    private static BatteryCommand SampleCommand(string id) => new(
        CommandId: id,
        Timestamp: MqttFixtures.Now,
        AssetId: "asset-1",
        Mode: CommandMode.Discharge,
        ActivePowerKw: 10,
        ReactivePowerKvar: 0,
        ValidUntil: MqttFixtures.Now + TimeSpan.FromSeconds(30),
        Reason: "test",
        Source: CommandSource.Optimization);

    private static async Task ReadFirstOrCancel(MqttTelemetrySource source, CancellationToken ct)
    {
        try
        {
            await foreach (var _ in source.ReadAsync(ct))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // expected when the test cancels the reader
        }
    }
}
