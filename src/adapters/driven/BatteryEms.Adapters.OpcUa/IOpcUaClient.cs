namespace BatteryEms.Adapters.OpcUa;

// Driven port for the OPC-UA protocol stack (plan-RM-M4-04 §4 Sub-
// Slice A, D-02). Wraps the chosen SDK behind a testable surface so
// FakeOpcUaClient can drive Sub-Slice-B unit tests without an actual
// server, and a future SDK swap (D-01 → Workstation.UaClient or
// commercial SDK) stays a one-place change.
//
// The port is IAsyncDisposable per D-09 — DisposeAsync closes the
// underlying session with a short OperationTimeout cap so shutdown
// can't block on a kaputte Verbindung. Idempotent.
public interface IOpcUaClient : IAsyncDisposable
{
    // True after a successful ConnectAsync and until DisconnectAsync /
    // DisposeAsync. False before the first connect.
    bool IsConnected { get; }

    // Establish a session against the configured endpoint. Throws on
    // unrecoverable connect failures (caller decides whether to
    // retry — Sub-Slice-B implements exponential backoff).
    Task ConnectAsync(CancellationToken cancellationToken);

    // Best-effort tear-down. Idempotent; calling on a disconnected
    // client is a no-op. Returns when the session is closed or the
    // client gives up (OperationTimeout).
    Task DisconnectAsync(CancellationToken cancellationToken);

    // Single-shot synchronous Read of one Node's current Value +
    // StatusCode. Used by direction=read mappings on the Sub-Slice-B
    // Telemetry-Source's tick path.
    Task<OpcUaReadResult> ReadAsync(
        string nodeId,
        CancellationToken cancellationToken);

    // Single-shot Write of one Node's value. The dataType drives
    // server-side encoding; mismatch with the actual server-side
    // OPC-UA type surfaces as a Bad-StatusCode in the result.
    Task<OpcUaWriteResult> WriteAsync(
        string nodeId,
        object value,
        OpcUaDataType dataType,
        CancellationToken cancellationToken);

    // Create a publishing-side subscription. The publishingIntervalMs
    // sets how often the server batches MonitoredItem notifications
    // into a publish-response. Per-item sampling intervals are set
    // independently via IOpcUaSubscription.AddMonitoredItem so the
    // master DoD's "MonitoringIntervalMs pro Knoten verwenden" maps
    // to OPC-UA-Spec without multi-subscription grouping.
    Task<IOpcUaSubscription> CreateSubscriptionAsync(
        int publishingIntervalMs,
        CancellationToken cancellationToken);
}

// Subscription handle returned by CreateSubscriptionAsync. Owns the
// MonitoredItems registered on it and exposes the consolidated
// notification stream as IAsyncEnumerable. IAsyncDisposable per D-09 —
// DisposeAsync deletes the subscription on the server.
public interface IOpcUaSubscription : IAsyncDisposable
{
    // Add a node to the subscription's MonitoredItem set. The
    // samplingIntervalMs is the OPC-UA SamplingInterval for this
    // particular item; values <= 0 mean "use the subscription's
    // PublishingInterval" (server-side default behaviour).
    void AddMonitoredItem(
        string nodeId,
        OpcUaDataType dataType,
        int samplingIntervalMs);

    // Pull-side stream of notifications from this subscription. The
    // server-pushed MonitoredItem updates land on this enumerator;
    // the consumer (Sub-Slice-B Telemetry-Source) drives the cycle.
    // Cancellation cooperatively ends the stream.
    IAsyncEnumerable<OpcUaNotification> NotificationsAsync(
        CancellationToken cancellationToken);
}
