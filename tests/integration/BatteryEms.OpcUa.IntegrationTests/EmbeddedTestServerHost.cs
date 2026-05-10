using System.Net;
using System.Net.Sockets;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace BatteryEms.OpcUa.IntegrationTests;

// Orchestriert den embedded OPC-UA-TestServer per programmatischer
// `ApplicationConfigurationBuilder`-Konfiguration (kein .Config.xml-
// File; alles im Test-Prozess). Endpunkt liegt auf einem freien
// loopback-TCP-Port; Security ist `MessageSecurityMode.None` mit
// auto-akzeptierten Untrusted-Zertifikaten — passt zum
// `Defaults.ForHilSimulator()`-Profile (D-04 + Sub-Slice D Test-Stub).
//
// IAsyncDisposable: stoppt den Server und disposed die telemetry,
// damit jede Test-Klasse sauber abräumt.
internal sealed class EmbeddedTestServerHost : IAsyncDisposable
{
    private readonly ITelemetryContext _telemetry;
    private readonly IDisposable? _telemetryDisposable;
    private readonly ApplicationInstance _application;
    private readonly BatteryTestNodeManagerFactory _nodeManagerFactory;
    private readonly string _pkiRoot;
    private BessEmsTestServer? _server;
    private bool _disposed;

    private EmbeddedTestServerHost(
        ITelemetryContext telemetry,
        IDisposable? telemetryDisposable,
        ApplicationInstance application,
        BatteryTestNodeManagerFactory factory,
        Uri endpointUrl,
        string pkiRoot)
    {
        _telemetry = telemetry;
        _telemetryDisposable = telemetryDisposable;
        _application = application;
        _nodeManagerFactory = factory;
        _pkiRoot = pkiRoot;
        EndpointUrl = endpointUrl;
    }

    public Uri EndpointUrl { get; }

    public BatteryTestNodeManager NodeManager =>
        _nodeManagerFactory.Manager
        ?? throw new InvalidOperationException(
            "TestServer not started yet — call StartAsync first.");

    public static async Task<EmbeddedTestServerHost> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var port = AcquireFreeLoopbackPort();
        var endpointUrl = new Uri($"opc.tcp://127.0.0.1:{port}/bess-ems-test");
        // Ein Test-Run-spezifischer pki-Root, damit parallele Test-
        // Klassen kein gemeinsames Cert-Verzeichnis kollidieren.
        var pkiRoot = Path.Combine(
            Path.GetTempPath(),
            "BatteryEms.OpcUa.IntegrationTests",
            $"pki-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pkiRoot);

        var telemetryRaw = DefaultTelemetry.Create(_ => { });
        var telemetryDisposable = telemetryRaw as IDisposable;

        var application = new ApplicationInstance(telemetryRaw)
        {
            ApplicationName = "BessEmsTestServer",
            ApplicationType = ApplicationType.Server,
        };

        var subject = "CN=BessEmsTestServer, O=BatteryEms, DC=localhost";
        var certs = ApplicationConfigurationBuilder
            .CreateDefaultApplicationCertificates(
                subject, CertificateStoreType.Directory, pkiRoot);

        await application
            .Build(
                applicationUri: $"urn:bess-ems:test-server:{Guid.NewGuid():N}",
                productUri: "urn:bess-ems:test-server")
            .AsServer([endpointUrl.ToString()])
            .AddUnsecurePolicyNone()
            .AddUserTokenPolicy(UserTokenType.Anonymous)
            .AddSecurityConfiguration(certs, pkiRoot)
            .SetAutoAcceptUntrustedCertificates(true)
            .CreateAsync(cancellationToken)
            .ConfigureAwait(false);

        await application
            .CheckApplicationInstanceCertificatesAsync(
                silent: true, CertificateFactory.DefaultLifeTime, cancellationToken)
            .ConfigureAwait(false);

        application.ApplicationConfiguration.CertificateValidator.CertificateValidation +=
            (sender, e) => e.Accept = true;

        var factory = new BatteryTestNodeManagerFactory();
        var host = new EmbeddedTestServerHost(
            telemetryRaw, telemetryDisposable, application, factory, endpointUrl, pkiRoot);
        await host.StartServerAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    public Task RestartAsync(CancellationToken cancellationToken)
        => RestartInternalAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        await StopServerAsync().ConfigureAwait(false);
        _telemetryDisposable?.Dispose();
        try
        {
            if (Directory.Exists(_pkiRoot))
            {
                Directory.Delete(_pkiRoot, recursive: true);
            }
        }
#pragma warning disable CA1031 // best-effort cleanup
        catch { }
#pragma warning restore CA1031
    }

    private async Task StartServerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _server = new BessEmsTestServer(_nodeManagerFactory);
        await _application.StartAsync(_server).ConfigureAwait(false);
    }

    private async Task StopServerAsync()
    {
        if (_server is null) { return; }
        var server = _server;
        _server = null;
        try { await _application.StopAsync().ConfigureAwait(false); }
#pragma warning disable CA1031 // Server stop is best-effort during teardown.
        catch { }
#pragma warning restore CA1031
        try { server.Dispose(); }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
    }

    private async Task RestartInternalAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopServerAsync().ConfigureAwait(false);
        // After Stop, the StandardServer is fully torn down; a fresh
        // instance is required to re-bind the endpoint listener. The
        // ApplicationInstance is reusable.
        await StartServerAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int AcquireFreeLoopbackPort()
    {
        // Bind a transient socket on port 0 and read back the assigned
        // port. The kernel guarantees the port is free for the lifetime
        // of the listener — a small race still exists with concurrent
        // binders, but the StandardServer below will surface the
        // collision as a startup error if it loses the race.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
