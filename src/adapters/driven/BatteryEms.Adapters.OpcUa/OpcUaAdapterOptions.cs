using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OpcUa;

// Operator-tunable + Security-slot-bearing options for the OPC-UA
// adapter (plan-RM-M4-04 §4 Sub-Slice A + D-04). EnsureValid runs the
// Security-Startup-Guard from D-04: SecurityMode=None requires an
// explicit AllowUnsecured=true plus a non-empty AllowUnsecuredReason,
// otherwise the host throws opcua-security-not-hardened at boot. The
// SecurityMode/SecurityPolicy slots exist today so M4-05 can layer the
// production-grade RuntimeProfile-aware Härtung on without an
// Options-Schema-Bruch.
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

    // Pre-M4-05 Security-Slots. Heute sitzt der Adapter auf `None` mit
    // dem AllowUnsecured-Startup-Guard (D-04); M4-05 ändert die Defaults
    // nicht, aber EnsureValid bekommt einen RuntimeProfile-Parameter.
    public OpcUaSecurityMode SecurityMode { get; init; } = OpcUaSecurityMode.None;
    public string SecurityPolicy { get; init; } = string.Empty;
    public bool AllowUnsecured { get; init; }
    public string? AllowUnsecuredReason { get; init; }

    // Run the Startup-Guard (D-04). Returns the same instance on
    // success so callers can chain (`new OpcUaAdapterOptions { ... }
    // .EnsureValid(logger)`). On the AllowUnsecured-success path, a
    // structured Warning is logged via LogUnsecuredOpcUaConnection
    // (EventId 4200) so the operator sees the reason in stdout before
    // the adapter starts wiring against an unsecured endpoint.
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

        // D-04 Startup-Guard. The bool-axis blocks unsecured operation
        // until the operator opts in twice (AllowUnsecured + non-empty
        // Reason). M4-05 layers RuntimeProfile-awareness on top.
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
}
