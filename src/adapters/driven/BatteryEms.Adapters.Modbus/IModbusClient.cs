namespace BatteryEms.Adapters.Modbus;

public interface IModbusClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<ushort[]> ReadHoldingRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken);

    Task WriteHoldingRegistersAsync(int unitId, int startAddress, ushort[] values, CancellationToken cancellationToken);
}
