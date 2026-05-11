using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-B Fixture: In-Process TestSidecar via
// Grpc.AspNetCore + Kestrel-UDS-Listener (ADR 0005 §6 In-Process-
// Mocking-Pattern, analog zur OPC-UA `EmbeddedTestServerHost`-Linie
// aus M4-04-D).
//
// Pro Fixture-Instanz ein Per-Test-UDS-Pfad in `Path.GetTempPath()`
// (analog zur OPC-UA Review-Fix H5-Konvention). HTTP/2-cleartext
// (h2c) auf der Pipe; kein TLS — Cross-Host-Pin-Variante mit mTLS
// lebt in Sub-Slice C.
internal sealed class EmbeddedOptimizationCoreSidecar : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _socketPath;
    private bool _disposed;

    public Uri Endpoint { get; }

    private EmbeddedOptimizationCoreSidecar(
        WebApplication app, string socketPath, Uri endpoint)
    {
        _app = app;
        _socketPath = socketPath;
        Endpoint = endpoint;
    }

    public static async Task<EmbeddedOptimizationCoreSidecar> StartAsync<TService>()
        where TService : BatteryEms.Adapters.OptimizationCore.Grpc.V1.OptimizationCore.OptimizationCoreBase
    {
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            "BatteryEms",
            "OptimizationCore",
            $"{Guid.NewGuid():N}.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o =>
        {
            o.ListenUnixSocket(socketPath, listen =>
            {
                listen.Protocols = HttpProtocols.Http2;
            });
        });
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<TService>();

        var app = builder.Build();
        app.MapGrpcService<TService>();
        await app.StartAsync().ConfigureAwait(false);

        var endpoint = new Uri($"unix://{socketPath}");
        return new EmbeddedOptimizationCoreSidecar(app, socketPath, endpoint);
    }

    public TService GetService<TService>()
        where TService : BatteryEms.Adapters.OptimizationCore.Grpc.V1.OptimizationCore.OptimizationCoreBase
        => _app.Services.GetRequiredService<TService>();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        try { await _app.StopAsync().ConfigureAwait(false); }
#pragma warning disable CA1031 // Best-effort teardown.
        catch { }
#pragma warning restore CA1031
        try { await _app.DisposeAsync().ConfigureAwait(false); }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
    }
}
