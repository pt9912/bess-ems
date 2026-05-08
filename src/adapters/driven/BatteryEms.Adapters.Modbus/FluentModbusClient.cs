using System.Net;
using System.Net.Sockets;
using FluentModbus;

namespace BatteryEms.Adapters.Modbus;

public sealed class FluentModbusClient : IModbusClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly ModbusTcpClient _client = new();

    // ModbusTcpClient is not thread-safe: reads and writes share the
    // same TCP request/response framing, so a concurrent ReadInput
    // from the telemetry source and a WriteHolding from the command
    // sink (both wired to the same singleton in DI) can interleave
    // their frames and produce "invalid response function code"
    // failures. The semaphore serialises every protocol call so the
    // shared-client wiring stays correct.
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

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

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        // Resolve before acquiring the gate so multiple concurrent
        // first-connect callers don't serialise on DNS. DNS itself
        // is async, the synchronous TCP connect is the only thing
        // that has to run under the gate.
        var address = await ResolveAddressAsync(_host, cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }
            // FluentModbus 5.x exposes only a synchronous Connect.
            // Offload to the thread pool so the caller's async
            // context isn't pinned through the TCP handshake; the
            // gate is still held during the await, so the per-client
            // serialisation guarantee that fixes the read/write race
            // (RM-M2-HIL race fix) stays intact.
            await Task.Run(
                () => _client.Connect(new IPEndPoint(address, _port), ModbusEndianness.BigEndian),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var memory = await _client
                .ReadHoldingRegistersAsync<ushort>(unitId, startAddress, count, cancellationToken)
                .ConfigureAwait(false);
            return CopyToArray(memory);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ushort[]> ReadInputRegistersAsync(int unitId, int startAddress, int count, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var memory = await _client
                .ReadInputRegistersAsync<ushort>(unitId, startAddress, count, cancellationToken)
                .ConfigureAwait(false);
            return CopyToArray(memory);
        }
        finally
        {
            _gate.Release();
        }
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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _client
                .WriteMultipleRegistersAsync(unitId, startAddress, values, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Disconnect();
        _client.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<IPAddress> ResolveAddressAsync(
        string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var direct))
        {
            return direct;
        }
        var addresses = await Dns
            .GetHostAddressesAsync(host, cancellationToken)
            .ConfigureAwait(false);
        foreach (var addr in addresses)
        {
            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                return addr;
            }
        }
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"Could not resolve host '{host}'.");
        }
        return addresses[0];
    }
}
