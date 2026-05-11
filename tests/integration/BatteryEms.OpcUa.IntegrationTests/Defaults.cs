using BatteryEms.Adapters.OpcUa;

namespace BatteryEms.OpcUa.IntegrationTests;

// Test-Fixture-Helper aus plan-RM-M4-04 §4 Sub-Slice D + plan-RM-M4-05-A:
// produziert eine `OpcUaAdapterOptions`-Instanz für den HilSimulator-
// None-Pfad. M4-05 schwenkt die Adapter-Defaults auf
// Production+SignAndEncrypt+Basic256Sha256 — Test-Defaults müssen daher
// explizit `RuntimeProfile=HilSimulator` plus `SecurityMode=None` plus
// `AllowUnsecured=true` plus einen nicht-leeren Reason setzen, damit
// `EnsureValid()` durchläuft (D-02 + D-03).
//
// Tests können die Defaults überschreiben — dies ist nur der
// Zero-Boilerplate-Pfad gegen den embedded Simulator. Der Production-
// Secure-Pfad bekommt einen eigenen Builder (`ForProductionSecure`)
// mit M4-05-C.
//
// Hinweis (Review-Fix L7): die Timing-Werte hier sind **bewusst**
// agressiver als die Production-Defaults aus `OpcUaAdapterOptions`
// (PollingInterval 100ms statt 1s, DefaultMonitoringIntervalMs 200ms
// statt 1000, ReconnectBackoffMax 2s statt 30s). Tests sollen schnell
// pinnen; die Production-Linie hat andere SLO-Anforderungen. Eine
// Derive-via-`with`-Strategie würde die intentional-aggressiven
// Test-Werte verschleiern.
internal static class Defaults
{
    public const string ForHilSimulatorReason = "hil-simulator-pre-m4-05";

    // Plan-RM-M4-05-C: Production-Secure-Builder. Triggert den
    // SignAndEncrypt-Pfad im OpcUaClient (AutoAccept=false, Trust-Store
    // backed). Der `TrustedServerCertificatesPath` bleibt null hier —
    // die `OpcUaTestServerFixture.EstablishSecureTrustAsync`-Bridge
    // schreibt die Server-Cert in den vom OpcUaClient angelegten
    // PKI-trusted-Store (Default unter `Path.GetTempPath()/BatteryEms/
    // OpcUa/pki/{Guid}/trusted/certs`). Eine Operator-Override-Variante
    // wird in Unit-Tests separat gepinnt (OpcUaClientTests).
    public static OpcUaAdapterOptions ForProductionSecure(Uri endpointUrl) =>
        new()
        {
            EndpointUrl = endpointUrl,
            SessionName = "bess-ems-test-secure",
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
            SecurityPolicy = OpcUaSecurityPolicies.Basic256Sha256,
            AllowUnsecured = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReadTimeout = TimeSpan.FromSeconds(5),
            PollingInterval = TimeSpan.FromMilliseconds(100),
            DefaultMonitoringIntervalMs = 200,
            KeepAliveInterval = TimeSpan.FromSeconds(2),
            ReconnectBackoffStart = TimeSpan.FromMilliseconds(200),
            ReconnectBackoffMax = TimeSpan.FromSeconds(2),
            SubscriptionChannelCapacity = 64,
        };

    public static OpcUaAdapterOptions ForHilSimulator(Uri endpointUrl) =>
        new()
        {
            EndpointUrl = endpointUrl,
            SessionName = "bess-ems-test",
            // M4-05-A D-03: explizit HilSimulator-Profile + None-Mode.
            // Production-Default würde mit SecurityMode=None werfen.
            RuntimeProfile = OpcUaRuntimeProfile.HilSimulator,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = ForHilSimulatorReason,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReadTimeout = TimeSpan.FromSeconds(5),
            PollingInterval = TimeSpan.FromMilliseconds(100),
            DefaultMonitoringIntervalMs = 200,
            // Plan-RM-M4-08 D-07: KeepAliveInterval explizit auf 2s
            // gepinnt (Production-Default ist 10s). Multi-Cycle-
            // Reconnect-Pin braucht deterministisches Disconnect-
            // Detection-Timing — mit 10s würde jeder Cycle bis zu 10s
            // hängen bevor die ConsecutiveFailures-Schwelle die
            // Recovery zündet. 2s plus PollingInterval=100ms × 2
            // (= ConsecutiveFailures-Schwelle) ergibt Recovery-Latenz
            // im low-Sekunden-Bereich.
            //
            // Bewusste Lücke: Production-Default 10s wird damit von
            // keinem Integration-Test exercised. Der Source/Recovery-
            // Pfad ist KeepAlive-agnostisch (Trigger sind
            // ConsecutiveFailures + !IsConnected), der 10s-Wert ist
            // unit-test-gepinnt in OpcUaAdapterOptionsTests.
            KeepAliveInterval = TimeSpan.FromSeconds(2),
            ReconnectBackoffStart = TimeSpan.FromMilliseconds(200),
            ReconnectBackoffMax = TimeSpan.FromSeconds(2),
            SubscriptionChannelCapacity = 64,
        };
}
