using BatteryEms.Adapters.Mqtt;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.Time;

namespace BatteryEms.Adapters.Mqtt.Tests;

internal static class MqttFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 5, 6, 9, 30, 0, TimeSpan.Zero);

    public static MqttMappingConfiguration SimulatorMapping() => new(
        ProfileName: "test",
        Topics: new List<MqttTopicMapping>
        {
            new("telemetry", "battery/{assetId}/telemetry", "subscribe", "json", false, "none"),
            new("status", "battery/{assetId}/status", "subscribe", "json", false, "none"),
            new("fault", "battery/{assetId}/fault", "subscribe", "json", false, "none"),
            new("command", "battery/{assetId}/command", "publish", "json", false, "none"),
            new("command_ack", "battery/{assetId}/command/ack", "subscribe", "json", false, "none"),
        });

    public static MqttAdapterOptions Defaults(string assetId = "asset-1") => new(
        BrokerHost: "127.0.0.1",
        BrokerPort: 1883,
        ClientId: "test-client",
        AssetId: assetId,
        ConnectTimeout: TimeSpan.FromSeconds(1),
        CommandAckTimeout: TimeSpan.FromMilliseconds(200));

    public sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }
}
