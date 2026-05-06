using System.Text;
using System.Text.Json;
using BatteryEms.Adapters.Mqtt;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

public sealed class MqttCommandSinkTests
{
    private static BatteryCommand SampleCommand(string id = "cmd-1") => new(
        CommandId: id,
        Timestamp: MqttFixtures.Now,
        AssetId: "asset-1",
        Mode: CommandMode.Discharge,
        ActivePowerKw: 25,
        ReactivePowerKvar: null,
        ValidUntil: MqttFixtures.Now.AddMinutes(1),
        Reason: "scheduled",
        Source: CommandSource.Schedule);

    [Fact]
    public async Task WriteAsync_publishes_command_and_returns_ok_after_matching_ack()
    {
        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var write = sink.WriteAsync(SampleCommand(), CancellationToken.None);

        await TestHelpers.WaitUntil(
            () => client.Publishes.Count > 0 && client.SubscribedTopics.Contains("battery/asset-1/command/ack"),
            TimeSpan.FromSeconds(1));

        await client.DeliverAsync(
            "battery/asset-1/command/ack",
            Encoding.UTF8.GetBytes(
                """{"command_id":"cmd-1","accepted":true,"dispatched_at":"2026-05-06T09:30:00+00:00","reason":"accepted"}"""));

        var result = await write;
        Assert.True(result.Success);
        Assert.Equal("accepted", result.Reason);

        Assert.Single(client.Publishes);
        var publish = client.Publishes[0];
        Assert.Equal("battery/asset-1/command", publish.Topic);
        Assert.False(publish.Retained);

        using var doc = JsonDocument.Parse(publish.Payload);
        Assert.Equal("cmd-1", doc.RootElement.GetProperty("command_id").GetString());
        Assert.Equal("Discharge", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(25, doc.RootElement.GetProperty("active_power_kw").GetDouble());
        Assert.Equal("Schedule", doc.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task WriteAsync_returns_ack_timeout_when_no_ack_arrives()
    {
        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var result = await sink.WriteAsync(SampleCommand(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("ack-timeout", result.Reason);
    }

    [Fact]
    public async Task WriteAsync_returns_ack_rejected_when_simulator_marks_not_accepted()
    {
        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var write = sink.WriteAsync(SampleCommand("cmd-rej"), CancellationToken.None);

        await TestHelpers.WaitUntil(
            () => client.Publishes.Count > 0 && client.SubscribedTopics.Contains("battery/asset-1/command/ack"),
            TimeSpan.FromSeconds(1));

        await client.DeliverAsync(
            "battery/asset-1/command/ack",
            Encoding.UTF8.GetBytes(
                """{"command_id":"cmd-rej","accepted":false,"dispatched_at":"2026-05-06T09:30:00+00:00","reason":"limit-violation"}"""));

        var result = await write;
        Assert.False(result.Success);
        Assert.Contains("limit-violation", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_ignores_acks_for_unrelated_command_ids()
    {
        var client = new FakeMqttClient();
        var options = MqttFixtures.Defaults() with { CommandAckTimeout = TimeSpan.FromMilliseconds(150) };
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), options, new MqttFixtures.FixedClock());

        var write = sink.WriteAsync(SampleCommand("cmd-real"), CancellationToken.None);

        await TestHelpers.WaitUntil(
            () => client.Publishes.Count > 0 && client.SubscribedTopics.Contains("battery/asset-1/command/ack"),
            TimeSpan.FromSeconds(1));

        await client.DeliverAsync(
            "battery/asset-1/command/ack",
            Encoding.UTF8.GetBytes(
                """{"command_id":"someone-else","accepted":true,"dispatched_at":"2026-05-06T09:30:00+00:00"}"""));

        var result = await write;
        Assert.False(result.Success);
        Assert.Equal("ack-timeout", result.Reason);
    }

    [Fact]
    public async Task WriteAsync_drops_malformed_ack_payloads_silently()
    {
        var client = new FakeMqttClient();
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var write = sink.WriteAsync(SampleCommand("cmd-mal"), CancellationToken.None);
        await TestHelpers.WaitUntil(
            () => client.Publishes.Count > 0 && client.SubscribedTopics.Contains("battery/asset-1/command/ack"),
            TimeSpan.FromSeconds(1));

        // Malformed JSON must not throw and must not satisfy the pending TCS;
        // the original command should still time out.
        await client.DeliverAsync("battery/asset-1/command/ack", Encoding.UTF8.GetBytes("not json"));

        var result = await write;
        Assert.False(result.Success);
        Assert.Equal("ack-timeout", result.Reason);
    }

    [Fact]
    public async Task WriteAsync_returns_failure_when_connect_throws()
    {
        var client = new FakeMqttClient
        {
            OnConnect = () => throw new InvalidOperationException("broker unreachable"),
        };
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var result = await sink.WriteAsync(SampleCommand(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.StartsWith("connect-failed", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_returns_failure_when_publish_throws()
    {
        var client = new FakeMqttClient
        {
            OnPublish = () => throw new InvalidOperationException("publish blew up"),
        };
        var sink = new MqttCommandSink(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        var result = await sink.WriteAsync(SampleCommand(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.StartsWith("publish-failed", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_throws_when_command_topic_missing()
    {
        var mapping = new BatteryEms.Application.Configuration.MqttMappingConfiguration("p",
            new List<BatteryEms.Application.Configuration.MqttTopicMapping>
            {
                new("command_ack", "battery/{assetId}/command/ack", "subscribe", "json", false, "none"),
            });
        Assert.Throws<InvalidOperationException>(() =>
            new MqttCommandSink(new FakeMqttClient(), mapping, MqttFixtures.Defaults(), new MqttFixtures.FixedClock()));
    }

    [Fact]
    public void Constructor_throws_when_ack_topic_missing()
    {
        var mapping = new BatteryEms.Application.Configuration.MqttMappingConfiguration("p",
            new List<BatteryEms.Application.Configuration.MqttTopicMapping>
            {
                new("command", "battery/{assetId}/command", "publish", "json", false, "none"),
            });
        Assert.Throws<InvalidOperationException>(() =>
            new MqttCommandSink(new FakeMqttClient(), mapping, MqttFixtures.Defaults(), new MqttFixtures.FixedClock()));
    }
}
