namespace BatteryEms.Adapters.Modbus;

public sealed record ModbusAdapterOptions(
    string Host,
    int Port,
    string AssetId,
    TimeSpan PollingInterval,
    TimeSpan ReadTimeout,
    int MaxConsecutiveFailures = 5)
{
    public static ModbusAdapterOptions Defaults(string host, int port, string assetId) => new(
        Host: host,
        Port: port,
        AssetId: assetId,
        PollingInterval: TimeSpan.FromSeconds(1),
        ReadTimeout: TimeSpan.FromSeconds(2),
        MaxConsecutiveFailures: 5);
}
