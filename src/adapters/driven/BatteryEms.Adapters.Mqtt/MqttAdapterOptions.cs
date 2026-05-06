namespace BatteryEms.Adapters.Mqtt;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttAdapterOptions(
    string BrokerHost,
    int BrokerPort,
    string ClientId,
    string AssetId,
    TimeSpan ConnectTimeout,
    TimeSpan CommandAckTimeout)
{
    public static MqttAdapterOptions Defaults(string brokerHost, int brokerPort, string clientId, string assetId) => new(
        BrokerHost: brokerHost,
        BrokerPort: brokerPort,
        ClientId: clientId,
        AssetId: assetId,
        ConnectTimeout: TimeSpan.FromSeconds(5),
        CommandAckTimeout: TimeSpan.FromSeconds(2));
}
