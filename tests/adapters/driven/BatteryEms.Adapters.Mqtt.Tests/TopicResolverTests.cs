using BatteryEms.Adapters.Mqtt;
using BatteryEms.Application.Configuration;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

public sealed class TopicResolverTests
{
    [Fact]
    public void Require_returns_topic_matching_name_and_direction()
    {
        var topic = TopicResolver.Require(MqttFixtures.SimulatorMapping(), "telemetry", "subscribe");
        Assert.Equal("battery/{assetId}/telemetry", topic.Topic);
    }

    [Fact]
    public void Require_throws_when_name_missing()
    {
        var mapping = new MqttMappingConfiguration("v1", "p", new List<MqttTopicMapping>
        {
            new("telemetry", "battery/{assetId}/telemetry", "subscribe", "json", false, "none"),
        });
        Assert.Throws<InvalidOperationException>(() =>
            TopicResolver.Require(mapping, "command", "publish"));
    }

    [Fact]
    public void Require_throws_when_direction_does_not_match()
    {
        // EMS-perspective semantics: a command_ack with direction='publish'
        // would mean the EMS publishes ACKs, which is wrong. Resolver must
        // refuse to silently match the wrong-direction topic.
        var mapping = new MqttMappingConfiguration("v1", "p", new List<MqttTopicMapping>
        {
            new("command_ack", "battery/{assetId}/command/ack", "publish", "json", false, "none"),
        });
        Assert.Throws<InvalidOperationException>(() =>
            TopicResolver.Require(mapping, "command_ack", "subscribe"));
    }

    [Theory]
    [InlineData("battery/{assetId}/telemetry", "asset-1", "battery/asset-1/telemetry")]
    [InlineData("battery/{assetId}/cmd/{assetId}", "x", "battery/x/cmd/x")]
    [InlineData("static/topic", "asset-1", "static/topic")]
    public void SubstituteAssetId_replaces_placeholder(string template, string assetId, string expected)
    {
        Assert.Equal(expected, TopicResolver.SubstituteAssetId(template, assetId));
    }
}
