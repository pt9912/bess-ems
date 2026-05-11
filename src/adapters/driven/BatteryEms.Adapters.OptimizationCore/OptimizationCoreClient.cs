using System.Net.Sockets;
using Grpc.Net.Client;

namespace BatteryEms.Adapters.OptimizationCore;

// Internal Wrapper um den `GrpcChannel` + die Grpc.Tools-generierten
// Client-Typen (`Grpc.V1.OptimizationCore.OptimizationCoreClient`).
//
// Lifecycle: ein Singleton pro Adapter-Registrierung. Hält die
// HTTP/2-Connection-Pool-Lebensdauer des Channels und exposiert die
// generierten Service-Stubs an den `OptimizationCoreScheduleOptimizer`.
// `DisposeAsync` ruft `GrpcChannel.ShutdownAsync` + Dispose.
//
// **Sub-Slice-B Wire-Foundation:** UDS-Support via
// `SocketsHttpHandler.ConnectCallback` (Loopback-Default aus ADR
// 0005 §4); plaintext-`http://`-Endpoints für Test-Profile;
// `https://` ist heute Default-TLS-Chain. **Sub-Slice C** layert
// mTLS-Cert-Material + Bearer-Token-`CallCredentials` drauf.
internal sealed class OptimizationCoreClient : IAsyncDisposable
{
    private readonly OptimizationCoreOptions _options;
    private GrpcChannel? _channel;
    private Grpc.V1.OptimizationCore.OptimizationCoreClient? _client;
    private SocketsHttpHandler? _httpHandler;
    private bool _disposed;

    public OptimizationCoreClient(OptimizationCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    // Test-Hook für Sub-Slice-B-Pins gegen den In-Process TestSidecar.
    // Nach `Connect` ist `Client` non-null; pre-Connect throws.
    internal Grpc.V1.OptimizationCore.OptimizationCoreClient Client =>
        _client
        ?? throw new InvalidOperationException(
            "optimization-core-client-not-connected: call ConnectAsync first.");

    internal bool IsConnected => _channel is not null && !_disposed;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_channel is not null) { return Task.CompletedTask; }

        var endpoint = _options.SidecarEndpoint;
        var (channelAddress, handler) = BuildChannelTransport(endpoint);
        var channelOptions = new GrpcChannelOptions();
        if (handler is not null)
        {
            channelOptions.HttpHandler = handler;
            _httpHandler = handler;
        }
        var channel = GrpcChannel.ForAddress(channelAddress, channelOptions);
        _channel = channel;
        _client = new Grpc.V1.OptimizationCore.OptimizationCoreClient(channel);
        return Task.CompletedTask;
    }

    // Plan-RM-M5-01-B: Endpoint-Scheme-spezifisches Transport-Setup.
    // - `unix://` → `SocketsHttpHandler.ConnectCallback` bindet ein
    //   `AF_UNIX`-Socket; gRPC läuft über HTTP/2-cleartext (h2c) auf
    //   der lokalen Pipe.
    // - `http://` → direkter `GrpcChannel.ForAddress`; nur in
    //   Test-Profile (EnsureValid rejected das in Production).
    // - `https://` → Default-TLS-Chain. mTLS-Cert-Material wird in
    //   Sub-Slice C über zusätzliche Handler-Konfiguration ergänzt.
    private static (string Address, SocketsHttpHandler? Handler) BuildChannelTransport(
        Uri endpoint)
    {
        if (string.Equals(endpoint.Scheme, "unix", StringComparison.OrdinalIgnoreCase))
        {
            var udsPath = endpoint.LocalPath;
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, ct) =>
                {
                    var socket = new Socket(
                        AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(
                            new UnixDomainSocketEndPoint(udsPath), ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };
            // GrpcChannel.ForAddress braucht eine http://-Adresse als
            // "Authority"-Marker; der ConnectCallback unterläuft den
            // DNS-Lookup und bindet auf den UDS-Socket.
            return ("http://uds-localhost", handler);
        }
        return (endpoint.ToString(), null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (_channel is not null)
        {
            try { await _channel.ShutdownAsync().ConfigureAwait(false); }
#pragma warning disable CA1031 // Adapter boundary — Dispose must not throw.
            catch { }
#pragma warning restore CA1031
            _channel.Dispose();
            _channel = null;
            _client = null;
        }
        _httpHandler?.Dispose();
        _httpHandler = null;
    }
}
