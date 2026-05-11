using BatteryEms.Adapters.OpcUa;
using Xunit;

namespace BatteryEms.OpcUa.IntegrationTests;

// xUnit IAsyncLifetime fixture: bringt einen embedded OPC-UA-TestServer
// pro Test-Klasse hoch und reisst ihn am Ende sauber ab. Pro Test-Klasse
// ein Server-Lifecycle, damit der StatusCode-Pin und der Reconnect-Pin
// keinen State an spätere Pins durchreichen.
//
// Plan-RM-M4-08 D-06: Per-class Fixture-Instanz statt collection-shared
// — `OpcUaIntegrationCollection` trägt absichtlich KEINEN
// `ICollectionFixture<OpcUaTestServerFixture>`, damit jede Test-Klasse
// (`OpcUaRoundtripTests`, `OpcUaNegativeTests`) ihre eigene Fixture
// bekommt. Multi-Cycle-Reconnect-State würde sonst zwischen Klassen
// bluten.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
    Justification = "xUnit instantiates the fixture via the IClassFixture<T> contract.")]
public sealed class OpcUaTestServerFixture : IAsyncLifetime
{
    private EmbeddedTestServerHost? _host;

    internal EmbeddedTestServerHost Host =>
        _host ?? throw new InvalidOperationException(
            "Fixture not initialized. xUnit didn't call InitializeAsync.");

    public async Task InitializeAsync()
    {
        _host = await EmbeddedTestServerHost.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }
    }

    // Plan-RM-M4-05-C: Trust-Bridge-Hook. Ruft `CertificateTrustBridge.
    // EstablishMutualTrustAsync` mit dem Fixture-Host und dem übergebenen
    // Client. Tests im Production-Secure-Pfad (`Defaults.ForProductionSecure`)
    // rufen das vor dem ersten `ConnectAsync` auf, damit der Trust greift.
    // Der Hook ist idempotent — mehrfacher Aufruf ist OK (Helper deduppt
    // über Thumbprint im Store).
    internal Task EstablishSecureTrustAsync(
        OpcUaClient client,
        CancellationToken cancellationToken = default)
        => CertificateTrustBridge.EstablishMutualTrustAsync(
            Host, client, cancellationToken);

    // Setzt alle Test-Knoten auf eine bekannte Baseline (Plan-RM-M4-08
    // M2/M7-Konsequenz). Beide Test-Klassen rufen das in ihrem
    // `IAsyncLifetime.InitializeAsync` auf, damit jeder Test mit
    // konsistenten Werten startet — der StatusCode-Pin lässt sonst
    // einen `Bad`-Status auf `Battery.Soc` zurück, der Subscribe-Pin
    // einen high-Wert, etc.
    internal void ResetNodeBaseline()
    {
        var nm = Host.NodeManager;
        nm.SetValue("Battery.Soc", 50.0f);
        nm.SetValue("Battery.ActivePower", 0.0f);
        nm.SetValue("Battery.ReactivePower", 0.0f);
        nm.SetValue("Battery.Temperature", 22.0f);
        nm.SetValue("Battery.FaultCode", (ushort)0);
        nm.SetValue("Battery.Setpoint.ActivePower", 0.0f);
        nm.SetValue("Battery.Setpoint.ReactivePower", 0.0f);
    }
}
