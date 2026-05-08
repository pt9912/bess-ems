using System.Net;
using System.Net.Sockets;
using FluentModbus;

namespace BatteryEms.Adapters.Modbus;

public sealed class FluentModbusClient : IModbusClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly ModbusTcpClient _client = new();

    public FluentModbusClient(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be in (0, 65535].");
        }

        _host = host;
        _port = port;
    }

    public bool IsConnected => _client.IsConnected;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var address = ResolveAddress(_host);
        _client.Connect(new IPEndPoint(address, _port), ModbusEndianness.BigEndian);
        return Task.CompletedTask;
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memory = await _client
            .ReadHoldingRegistersAsync<ushort>(unitId, startAddress, count, cancellationToken)
            .ConfigureAwait(false);
        return CopyToArray(memory);
    }

    public async Task<ushort[]> ReadInputRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memory = await _client
            .ReadInputRegistersAsync<ushort>(unitId, startAddress, count, cancellationToken)
            .ConfigureAwait(false);
        return CopyToArray(memory);
    }

    private static ushort[] CopyToArray(Memory<ushort> memory)
    {
        var span = memory.Span;
        var result = new ushort[span.Length];
        for (var i = 0; i < span.Length; i++)
        {
            result[i] = span[i];
        }
        return result;
    }

    public async Task WriteHoldingRegistersAsync(int unitId, int startAddress, ushort[] values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        cancellationToken.ThrowIfCancellationRequested();

        await _client
            .WriteMultipleRegistersAsync(unitId, startAddress, values, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _client.Disconnect();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IPAddress ResolveAddress(string host)
    {
        if (IPAddress.TryParse(host, out var direct))
        {
            return direct;
        }
        var entry = Dns.GetHostEntry(host);
        foreach (var addr in entry.AddressList)
        {
            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                return addr;
            }
        }
        if (entry.AddressList.Length == 0)
        {
            throw new InvalidOperationException($"Could not resolve host '{host}'.");
        }
        return entry.AddressList[0];
    }
}
