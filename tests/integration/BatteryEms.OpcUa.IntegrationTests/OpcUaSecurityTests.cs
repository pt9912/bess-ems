using BatteryEms.Adapters.OpcUa;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OpcUa.IntegrationTests;

// Plan-RM-M4-05 §3 + §4 Sub-Slice D: 6 gepinnte Security-Pins gegen den
// Embedded TestServer. Test-Layout erbt von M4-08-A D-06: eigene Klasse
// (per-class Fixture), `[Collection("OpcUa Integration")]` für die
// Serialisierung gegen `OpcUaRoundtripTests`/`OpcUaNegativeTests`. Die
// Production-Secure-Pins benutzen `Defaults.ForProductionSecure(...)`
// plus `_fixture.EstablishSecureTrustAsync(client)` (Cert-Trust-Bridge
// aus Sub-Slice C); die EnsureValid-only-Pins arbeiten gegen
// `OpcUaAdapterOptions` direkt.
[Trait("Category", "Integration")]
[Collection("OpcUa Integration")]
public sealed class OpcUaSecurityTests
    : IClassFixture<OpcUaTestServerFixture>, IAsyncLifetime
{
    private readonly OpcUaTestServerFixture _fixture;
    private const string SocNodeId = "ns=2;s=Battery.Soc";

    public OpcUaSecurityTests(OpcUaTestServerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.ResetNodeBaseline();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Pin 1 (§3, plan-RM-M4-05-D): Defaults.ForProductionSecure + Trust-
    // Bridge → ConnectAsync succeeds, ReadAsync gibt einen Sample mit
    // Good-StatusCode zurück. Das ist der primäre Production-Pfad-Beweis.
    [Fact]
    public async Task Secure_handshake_signandencrypt_succeeds_against_test_server()
    {
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Soc", 73.0f);
        var options = Defaults.ForProductionSecure(host.EndpointUrl);
        await using var client = new OpcUaClient(options);

        await _fixture.EstablishSecureTrustAsync(client);
        await client.ConnectAsync(default);

        var result = await client.ReadAsync(SocNodeId, default);
        Assert.Equal(0u, result.StatusCode); // Good
        Assert.True(client.IsConnected);
    }

    // Pin 2 (§3): SignAndEncrypt → Sign-Mode-Variante. Same trust-bridge,
    // anderer Endpoint vom Server.
    [Fact]
    public async Task Secure_handshake_sign_mode_succeeds_against_test_server()
    {
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Soc", 41.0f);
        var options = Defaults.ForProductionSecure(host.EndpointUrl) with
        {
            SecurityMode = OpcUaSecurityMode.Sign,
        };
        await using var client = new OpcUaClient(options);

        await _fixture.EstablishSecureTrustAsync(client);
        await client.ConnectAsync(default);

        var result = await client.ReadAsync(SocNodeId, default);
        Assert.Equal(0u, result.StatusCode);
        Assert.True(client.IsConnected);
    }

    // Pin 3 (§3): nicht-allowlisted Policy → EnsureValid wirft am
    // Konstruktor-Pfad (kein Connect nötig). Operator setzt z.B.
    // Basic128Rsa15 (deprecated, bewusst nicht in der M4-Start-
    // Allowlist).
    [Fact]
    public void Non_allowlisted_policy_throws_at_construction()
    {
        var options = Defaults.ForProductionSecure(_fixture.Host.EndpointUrl) with
        {
            SecurityPolicy =
                "http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15",
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.EnsureValid(NullLogger<OpcUaAdapterOptions>.Instance));
        Assert.Contains(
            "opcua-security-policy-not-allowlisted",
            ex.Message,
            StringComparison.Ordinal);
    }

    // Pin 4 (§3): Production-Fail-Closed bei SecurityMode=None — auch
    // mit AllowUnsecured=true + Reason (heute-Test-Konfig!). Der Bool-
    // Override ist im Production-Profile bewusst nicht ausreichend
    // (M4-05 D-02).
    [Fact]
    public void Production_profile_with_unsecured_mode_throws_at_construction()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = _fixture.Host.EndpointUrl,
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = Defaults.ForHilSimulatorReason,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.EnsureValid(NullLogger<OpcUaAdapterOptions>.Instance));
        Assert.Contains(
            "opcua-security-not-hardened-in-production",
            ex.Message,
            StringComparison.Ordinal);
    }

    // Pin 5 (§3): HilSimulator-Override bleibt valider Pfad — heutiges
    // Pre-M4-05-Verhalten für Test-Profile bleibt erhalten. EventId
    // 4200 wird im Test-Logger emittiert.
    [Fact]
    public void Hil_simulator_profile_with_unsecured_mode_passes()
    {
        var spy = new SpyLogger();
        var options = Defaults.ForHilSimulator(_fixture.Host.EndpointUrl);

        var result = options.EnsureValid(spy);

        Assert.Same(options, result);
        Assert.Contains(spy.Records,
            r => r.Level == LogLevel.Warning && r.EventId == 4200);
    }

    // Pin 6 (§3): Trust-Store-Miss → ConnectAsync wirft
    // `opcua-server-certificate-not-trusted`. ForProductionSecure ohne
    // `EstablishSecureTrustAsync` — der Client hat die Server-Cert nicht
    // im Trusted-Store, und mit `SetAutoAcceptUntrustedCertificates(false)`
    // im Production-Pfad rejected der SDK-Validator.
    [Fact]
    public async Task Production_profile_without_trusted_server_certificate_fails()
    {
        var options = Defaults.ForProductionSecure(_fixture.Host.EndpointUrl);
        await using var client = new OpcUaClient(options);

        // BEWUSST kein EstablishSecureTrustAsync — der Client trustet
        // die Server-Cert nicht.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectAsync(cts.Token));
        Assert.Contains(
            "opcua-server-certificate-not-trusted",
            ex.Message,
            StringComparison.Ordinal);
        Assert.False(client.IsConnected);
    }

    private sealed class SpyLogger : ILogger
    {
        public List<Record> Records { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state)!;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId,
            TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Records.Add(new Record(logLevel, eventId.Id, formatter(state, exception)));
        }

        public sealed record Record(LogLevel Level, int EventId, string Message);
    }
}
