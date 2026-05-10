using BatteryEms.Adapters.OpcUa;

namespace BatteryEms.OpcUa.IntegrationTests;

// Test-Fixture-Helper aus plan-RM-M4-04 §4 Sub-Slice D: produziert eine
// `OpcUaAdapterOptions`-Instanz, die schon `AllowUnsecured=true` plus
// einen nicht-leeren `AllowUnsecuredReason` trägt. Ohne diese
// Vorbelegung würde `EnsureValid()` mit `opcua-security-not-hardened`
// werfen, bevor irgendein Pin zünden kann (D-04 Konsequenz).
//
// Tests können die Defaults überschreiben — dies ist nur der
// Zero-Boilerplate-Pfad gegen den embedded Simulator.
internal static class Defaults
{
    public const string ForHilSimulatorReason = "hil-simulator-pre-m4-05";

    public static OpcUaAdapterOptions ForHilSimulator(Uri endpointUrl) =>
        new()
        {
            EndpointUrl = endpointUrl,
            SessionName = "bess-ems-test",
            AllowUnsecured = true,
            AllowUnsecuredReason = ForHilSimulatorReason,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReadTimeout = TimeSpan.FromSeconds(5),
            PollingInterval = TimeSpan.FromMilliseconds(100),
            DefaultMonitoringIntervalMs = 200,
            ReconnectBackoffStart = TimeSpan.FromMilliseconds(200),
            ReconnectBackoffMax = TimeSpan.FromSeconds(2),
            SubscriptionChannelCapacity = 64,
        };
}
