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
        // Plan-RM-M5-01-C: UDS-Mode-Enforcement im Production-Profile.
        // Operator stellt einen Per-Service-Socket mit Filesystem-Perms
        // (Mode=0600, Owner=bess-Service) bereit (ADR 0005 §4 Default).
        // Andere Perms → Boot-Fehler `optimization-core-uds-permissions-
        // not-locked`, damit ein versehentlich world-readable Socket
        // niemals produktiv gefahren wird.
        EnsureUdsPermissionsLockedIfRequired(endpoint, _options.RuntimeProfile);

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

    private static void EnsureUdsPermissionsLockedIfRequired(
        Uri endpoint, OptimizationCoreRuntimeProfile profile)
    {
        if (profile != OptimizationCoreRuntimeProfile.Production) { return; }
        if (!string.Equals(endpoint.Scheme, "unix", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var path = endpoint.LocalPath;
        if (!File.Exists(path))
        {
            // Socket noch nicht angelegt — Sidecar fährt eventuell
            // parallel hoch. Wir lassen den Connect am gRPC-Layer
            // fehlschlagen (Unavailable-Outcome via StatusMapper) statt
            // hier einen eigenen Boot-Fehler zu werfen.
            return;
        }
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // Filesystem-Mode-Bits sind Unix-spezifisch; auf Windows
            // ist die Production-Topologie ohnehin unrealistisch
            // (Kestrel-UDS-Listener ist .NET-8+ Linux-Pfad).
            return;
        }
        var mode = File.GetUnixFileMode(path);
        const UnixFileMode UserRwOnly =
            UnixFileMode.UserRead | UnixFileMode.UserWrite;
        const UnixFileMode UserGroupRw =
            UserRwOnly | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;
        // Akzeptiert: Mode 0600 (Owner-Only) und Mode 0660 (Owner +
        // Group-Member; brauchbar wenn der Sidecar als anderer User
        // läuft und nur der bess-Gruppe Zugriff gewährt). Alles
        // weitere fail-closed.
        if (mode != UserRwOnly && mode != UserGroupRw)
        {
            var octal = Convert.ToString((int)mode, toBase: 8).PadLeft(3, '0');
            throw new InvalidOperationException(
                $"optimization-core-uds-permissions-not-locked: socket "
                + $"`{path}` has mode 0{octal} but Production "
                + "requires 0600 (Owner-Only) or 0660 (Owner+Group). "
                + "Switch RuntimeProfile to HilSimulator/Development if "
                + "you are testing locally without locked-down perms.");
        }
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
