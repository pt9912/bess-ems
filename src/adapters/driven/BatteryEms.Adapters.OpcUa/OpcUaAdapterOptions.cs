using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OpcUa;

// Operator-tunable + Security-slot-bearing options for the OPC-UA
// adapter (plan-RM-M4-04 §4 Sub-Slice A + D-04; M4-05-A erweitert um
// RuntimeProfile-Awareness und schwenkt die Production-Defaults auf
// `SignAndEncrypt` + `Basic256Sha256`). `EnsureValid` macht den
// Production-Pfad fail-closed:
//
// * `RuntimeProfile=Production` + `SecurityMode=None` ⇒ harter
//   Startup-Fehler `opcua-security-not-hardened-in-production`,
//   unabhängig von `AllowUnsecured` (M4-05 D-02).
// * `SecurityMode!=None` + `SecurityPolicy` nicht in der Allowlist ⇒
//   `opcua-security-policy-not-allowlisted` (D-04).
// * `SecurityMode!=None` + `AllowUnsecured=true` ⇒
//   `opcua-allow-unsecured-with-secure-mode-inconsistent`.
// * `RuntimeProfile=HilSimulator|Development` + `SecurityMode=None`
//   geht weiterhin durch den AllowUnsecured-Bool-Guard (Pre-M4-05-
//   Verhalten für Test-Profile).
public sealed record OpcUaAdapterOptions
{
    // System.Uri statt string per CA1056 — die OPC-UA-Endpoint-URL
    // ist per Spec ein opc.tcp://-URI; der SDK akzeptiert ohnehin
    // System.Uri auf der Session-Konstruktor-Linie.
    public required Uri EndpointUrl { get; init; }

    public string SessionName { get; init; } = "bess-ems";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(5);

    // Source-Loop-Cadence (analog zu ModbusAdapterOptions.PollingInterval):
    // pro Iteration der `IAsyncEnumerable<BatteryTelemetry>`-Schleife
    // wartet der Source diese Zeit, bevor er die nächste Probe
    // assembliert + emittiert. Subscribe-Notifications werden in
    // diesem Intervall aus dem Channel gedraint.
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ReconnectBackoffStart { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReconnectBackoffMax { get; init; } = TimeSpan.FromSeconds(30);

    // Fallback wenn ein Subscribe-Mapping-Knoten kein eigenes
    // MonitoringIntervalMs trägt (plan-RM-M4-04 §4 Sub-Slice B). Wird
    // gleichzeitig als Subscription-Publishing-Interval verwendet
    // (eine Subscription pro Adapter, MonitoredItems mit per-Item-
    // Sampling-Interval).
    public int DefaultMonitoringIntervalMs { get; init; } = 1000;

    // Bounded-Channel-Capacity für den Subscribe-Stream (D-03). Bei
    // Channel-voll wird das älteste Sample gedropt und der sticky
    // Overflow-Flag in der Telemetry-Source gesetzt.
    public int SubscriptionChannelCapacity { get; init; } = 256;

    // Plan-RM-M4-05 §3: adapter-lokales RuntimeProfile-Field. Default
    // `Production` macht den Production-Code-Pfad fail-closed gegen
    // Konfigurations-Drift; Test-Profile setzen das explizit auf
    // `HilSimulator` (siehe IntegrationTests/Defaults.cs).
    public OpcUaRuntimeProfile RuntimeProfile { get; init; } = OpcUaRuntimeProfile.Production;

    // M4-05 Default-Schwenk: SignAndEncrypt statt None, Basic256Sha256
    // statt leer. Eine Operator-Konfiguration ohne explizite Profile-/
    // Mode-Wahl bekommt damit den sicheren Default (Master-DoD-D-03).
    public OpcUaSecurityMode SecurityMode { get; init; } = OpcUaSecurityMode.SignAndEncrypt;
    public string SecurityPolicy { get; init; } = OpcUaSecurityPolicies.Basic256Sha256;
    public bool AllowUnsecured { get; init; }
    public string? AllowUnsecuredReason { get; init; }

    // Optionaler Operator-Override für das App-Cert-Subject. Wenn leer,
    // leitet der OpcUaClient es aus SessionName ab (heute hard-coded
    // `CN={SessionName}, O=BatteryEms, DC=localhost`). Operator kann
    // ein Vendor-konformes Subject vorgeben (M4-05-B).
    public string? ApplicationCertificateSubject { get; init; }

    // Optionaler Operator-Override für den Trusted-Server-Cert-Store-
    // Pfad. Default leer ⇒ Adapter legt einen Store unter dem
    // per-Instanz-PKI-Root an (Production verlangt dann eine Trust-
    // Provisioning-Bridge, sonst schlägt der Connect mit
    // `opcua-server-certificate-not-trusted` fehl). Operator-Pfad
    // ermöglicht Pre-Deployment-Cert-Provisioning.
    public string? TrustedServerCertificatesPath { get; init; }

    // Run the Startup-Guard. Returns the same instance on success so
    // callers can chain. EventId 4200 (existing) markiert den
    // AllowUnsecured-Override; 4221 markiert den Production-Sicher-
    // Pfad; 4222 bestätigt die Allowlist-Auswahl. Reihenfolge der
    // Validations: Argument-Checks → numerische Slots → Profile/
    // Security-Achse.
    public OpcUaAdapterOptions EnsureValid(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(EndpointUrl);

        // Self-validation against the type's own properties — not
        // against EnsureValid's parameters — so the throws use
        // InvalidOperationException (no paramName argument). The
        // EndpointUrl ArgumentException above remains correct because
        // the property is the analogue of a constructor parameter.
        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ConnectTimeout must be positive (got {ConnectTimeout}).");
        }
        if (ReadTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ReadTimeout must be positive (got {ReadTimeout}).");
        }
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"PollingInterval must be positive (got {PollingInterval}).");
        }
        if (KeepAliveInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"KeepAliveInterval must be positive (got {KeepAliveInterval}).");
        }
        if (ReconnectBackoffStart <= TimeSpan.Zero
            || ReconnectBackoffMax < ReconnectBackoffStart)
        {
            throw new InvalidOperationException(
                $"ReconnectBackoffStart ({ReconnectBackoffStart}) must be positive and "
                + $"<= ReconnectBackoffMax ({ReconnectBackoffMax}).");
        }
        if (DefaultMonitoringIntervalMs <= 0)
        {
            throw new InvalidOperationException(
                $"DefaultMonitoringIntervalMs must be positive (got {DefaultMonitoringIntervalMs}).");
        }
        if (SubscriptionChannelCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"SubscriptionChannelCapacity must be positive (got {SubscriptionChannelCapacity}).");
        }

        // M4-05 D-02: Production + None ist ein harter Startup-Fehler,
        // unabhängig von AllowUnsecured. Der Bool-Guard ist im
        // Production-Profile bewusst nicht ausreichend; wer trotzdem
        // unsecured fahren muss, setzt RuntimeProfile auf HilSimulator
        // oder Development (bewusst-sichtbar in der Deploy-Konfig).
        if (RuntimeProfile == OpcUaRuntimeProfile.Production
            && SecurityMode == OpcUaSecurityMode.None)
        {
            throw new InvalidOperationException(
                "opcua-security-not-hardened-in-production: "
                + "RuntimeProfile=Production requires SecurityMode=Sign or "
                + "SignAndEncrypt. AllowUnsecured is not a valid override in "
                + "Production — switch RuntimeProfile to Development or "
                + "HilSimulator if you must run against an unsecured endpoint "
                + "(e.g. a legacy server without cert support).");
        }

        // M4-05 (c): Sign|SignAndEncrypt darf nicht mit AllowUnsecured=true
        // kombiniert sein — das ist eine Konfigurations-Inkonsistenz, kein
        // operativ valider Pfad.
        if (SecurityMode != OpcUaSecurityMode.None && AllowUnsecured)
        {
            throw new InvalidOperationException(
                "opcua-allow-unsecured-with-secure-mode-inconsistent: "
                + $"SecurityMode={SecurityMode} cannot be combined with "
                + "AllowUnsecured=true. Pick exactly one: a secure mode "
                + "(Sign or SignAndEncrypt) or an unsecured opt-in via "
                + "SecurityMode=None plus AllowUnsecured=true plus a "
                + "non-empty AllowUnsecuredReason.");
        }

        // M4-05 D-04: jede Sign|SignAndEncrypt-Konfiguration muss eine
        // allowlistete Policy nennen. `Basic256Sha256` ist der heutige
        // M4-Start-Eintrag; jede Erweiterung verlangt einen Plan-Slice
        // (F-17) und einen Code-Change in `OpcUaSecurityPolicies`.
        if (SecurityMode != OpcUaSecurityMode.None
            && !OpcUaSecurityPolicies.IsAllowed(SecurityPolicy))
        {
            throw new InvalidOperationException(
                $"opcua-security-policy-not-allowlisted: SecurityPolicy="
                + $"'{SecurityPolicy}' is not in the M4-05 allowlist. "
                + "Allowlisted: Basic256Sha256. Adding a policy requires "
                + "an F-17 plan-slice plus an OpcUaSecurityPolicies code "
                + "change — see note-RM-M4-followups.md.");
        }

        // D-04 (existing) Startup-Guard für den None-Pfad. Nach den
        // Production-Checks: Test-Profile dürfen mit None+AllowUnsecured
        // weiterfahren (Pre-M4-05-Verhalten erhalten).
        if (SecurityMode == OpcUaSecurityMode.None)
        {
            if (!AllowUnsecured)
            {
                throw new InvalidOperationException(
                    "opcua-security-not-hardened: SecurityMode=None requires "
                    + "AllowUnsecured=true plus a non-empty AllowUnsecuredReason. "
                    + "Set both fields explicitly to opt in to unsecured operation "
                    + "(e.g. against a HIL simulator or a Pre-M4-05 endpoint).");
            }
            if (string.IsNullOrWhiteSpace(AllowUnsecuredReason))
            {
                throw new InvalidOperationException(
                    "opcua-security-not-hardened: AllowUnsecured=true requires a "
                    + "non-empty AllowUnsecuredReason describing why an unsecured "
                    + "connection is acceptable in this deployment.");
            }
            OpcUaAdapterOptionsLog.LogUnsecuredOpcUaConnection(
                logger, EndpointUrl, AllowUnsecuredReason!);
        }
        else
        {
            // Secure-Pfad: structured Information-Markers, damit der
            // Operator im stdout-Log sieht, mit welchem Profile und
            // welcher Policy der Adapter hochfährt.
            OpcUaAdapterOptionsLog.LogSecureProfileEstablished(
                logger, RuntimeProfile, SecurityMode, EndpointUrl);
            OpcUaAdapterOptionsLog.LogAllowlistedPolicyAccepted(
                logger, SecurityPolicy);
        }

        return this;
    }
}

internal static partial class OpcUaAdapterOptionsLog
{
    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning,
        Message = "opcua adapter starting unsecured against {EndpointUrl}: {Reason}")]
    public static partial void LogUnsecuredOpcUaConnection(
        ILogger logger,
        Uri endpointUrl,
        string reason);

    [LoggerMessage(EventId = 4221, Level = LogLevel.Information,
        Message = "opcua adapter starting with secure profile {RuntimeProfile} mode={SecurityMode} against {EndpointUrl}")]
    public static partial void LogSecureProfileEstablished(
        ILogger logger,
        OpcUaRuntimeProfile runtimeProfile,
        OpcUaSecurityMode securityMode,
        Uri endpointUrl);

    [LoggerMessage(EventId = 4222, Level = LogLevel.Information,
        Message = "opcua adapter accepted allowlisted security policy {SecurityPolicy}")]
    public static partial void LogAllowlistedPolicyAccepted(
        ILogger logger,
        string securityPolicy);
}
