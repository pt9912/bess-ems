namespace BatteryEms.Adapters.Modbus;

public interface IModbusClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<ushort[]> ReadHoldingRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken);

    // RM-M2-HIL-02: input registers (Modbus FC04) for read-only
    // measurement points on PCS/grid-side devices (HIL profile).
    // Writes never go to input registers — there is no FC for that —
    // so the port stays read-only.
    Task<ushort[]> ReadInputRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken);

    Task WriteHoldingRegistersAsync(int unitId, int startAddress, ushort[] values, CancellationToken cancellationToken);
}
