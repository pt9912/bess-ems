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
// **Sub-Slice-A-Skelett:** Channel-Konstruktion + Lifecycle sind in A
// gelegt; die konkrete Channel-Konfiguration (UDS via
// `SocketsHttpHandler.ConnectCallback`, mTLS-Cert-Bindings, Bearer-
// Token-`CallCredentials`-Interceptor) landet in **RM-M5-01-C**
// (Security-Pfad). Heute baut `Connect` einen Default-Channel, der
// für plaintext-Endpoints (`http://`) und UDS (`unix://`) funktioniert;
// `https://` ohne mTLS-Materials läuft auf den .NET-Default-Trust-
// Chain — Production-Härtung folgt mit Sub-Slice C.
internal sealed class OptimizationCoreClient : IAsyncDisposable
{
    private readonly OptimizationCoreOptions _options;
    private GrpcChannel? _channel;
    private Grpc.V1.OptimizationCore.OptimizationCoreClient? _client;
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

    // Sub-Slice-A-Skelett: konkrete Channel-Konstruktion + Health-Probe
    // landet in RM-M5-01-B/C. Today returns a minimal default channel
    // that works for the TestSidecar-Plaintext-Pfad in Sub-Slice B's
    // In-Process-Fixture-Tests. Production-mTLS-Konfiguration wird in
    // Sub-Slice C ergänzt.
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_channel is not null) { return Task.CompletedTask; }

        var channel = GrpcChannel.ForAddress(_options.SidecarEndpoint);
        _channel = channel;
        _client = new Grpc.V1.OptimizationCore.OptimizationCoreClient(channel);
        return Task.CompletedTask;
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
    }
}
