using BatteryEms.Adapters.Modbus;

namespace BatteryEms.Adapters.Modbus.Tests;

internal sealed class FakeModbusClient : IModbusClient
{
    public bool IsConnected { get; private set; }

    public Dictionary<int, ushort[]> ReadResponses { get; } = new();

    // RM-M2-HIL-02: separate canned responses for FC04 input
    // registers; tests use this to assert that ModbusTelemetrySource
    // routed the register to the correct read function.
    public Dictionary<int, ushort[]> InputReadResponses { get; } = new();

    public List<(int UnitId, int Address, ushort[] Values)> Writes { get; } = new();

    public List<(string Table, int Address, int Count)> Reads { get; } = new();

    public Func<Task>? OnRead { get; set; }

    public Func<Task>? OnWrite { get; set; }

    public Func<Task>? OnConnect { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (OnConnect is not null)
        {
            return Connect();
        }
        IsConnected = true;
        return Task.CompletedTask;

        async Task Connect()
        {
            await OnConnect!.Invoke().ConfigureAwait(false);
            IsConnected = true;
        }
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken)
    {
        if (OnRead is not null)
        {
            await OnRead.Invoke().ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Reads.Add(("holding", startAddress, count));
        if (!ReadResponses.TryGetValue(startAddress, out var response))
        {
            return new ushort[count];
        }
        return response;
    }

    public async Task<ushort[]> ReadInputRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken)
    {
        if (OnRead is not null)
        {
            await OnRead.Invoke().ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Reads.Add(("input", startAddress, count));
        if (!InputReadResponses.TryGetValue(startAddress, out var response))
        {
            return new ushort[count];
        }
        return response;
    }

    public async Task WriteHoldingRegistersAsync(int unitId, int startAddress, ushort[] values, CancellationToken cancellationToken)
    {
        if (OnWrite is not null)
        {
            await OnWrite.Invoke().ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Writes.Add((unitId, startAddress, values));
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
