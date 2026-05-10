using Xunit;

namespace BatteryEms.OpcUa.IntegrationTests;

// xUnit IAsyncLifetime fixture: bringt einen embedded OPC-UA-TestServer
// pro Test-Klasse hoch und reisst ihn am Ende sauber ab. Pro Test-Klasse
// ein Server-Lifecycle, damit der StatusCode-Pin und der Reconnect-Pin
// keinen State an spätere Pins durchreichen.
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
}
