using System.Threading.Channels;

namespace BatteryEms.Adapters.OpcUa.Tests;

// Test-stub implementation of IOpcUaClient for Sub-Slice A/B/C unit
// tests (plan-RM-M4-04 §4 Sub-Slice A). In-memory node map with
// scriptable per-node Value + StatusCode; subscriptions are
// IOpcUaSubscription instances backed by Channel<OpcUaNotification>
// that the test pushes notifications into.
//
// The fake is intentionally permissive: it does NOT validate NodeId
// format, dataType-to-value coercion, or session-state preconditions.
// Tests configure it via SetValue / SetStatusCode / PushNotification
// helpers and assert against the calls the System Under Test makes.
public sealed class FakeOpcUaClient : IOpcUaClient
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FakeNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<FakeOpcUaSubscription> _subscriptions = new();
    private readonly List<FakeWriteRecord> _writes = new();
    private bool _connected;
    private bool _disposed;

    public bool IsConnected
    {
        get { lock (_gate) { return _connected && !_disposed; } }
    }

    // Last-write recorder for assertion convenience.
    public IReadOnlyList<FakeWriteRecord> Writes
    {
        get { lock (_gate) { return _writes.ToArray(); } }
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            _connected = true;
        }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _connected = false;
        }
        return Task.CompletedTask;
    }

    public Task<OpcUaReadResult> ReadAsync(
        string nodeId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfNotConnected();
            var node = GetOrCreate(nodeId);
            return Task.FromResult(new OpcUaReadResult(
                NodeId: nodeId,
                Value: node.Value,
                StatusCode: node.StatusCode,
                SourceTimestamp: node.SourceTimestamp));
        }
    }

    public Task<OpcUaWriteResult> WriteAsync(
        string nodeId,
        object value,
        OpcUaDataType dataType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfNotConnected();
            var node = GetOrCreate(nodeId);
            // Default: write succeeds with Good unless a test pre-set
            // a write-side StatusCode.
            var resultStatus = node.WriteStatusCode ?? 0u;
            _writes.Add(new FakeWriteRecord(nodeId, value, dataType, resultStatus));
            if (resultStatus == 0u)
            {
                node.Value = value;
                node.StatusCode = 0u;
                node.SourceTimestamp = DateTimeOffset.UtcNow;
            }
            return Task.FromResult(new OpcUaWriteResult(nodeId, resultStatus));
        }
    }

    public Task<IOpcUaSubscription> CreateSubscriptionAsync(
        int publishingIntervalMs,
        CancellationToken cancellationToken)
    {
        if (publishingIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publishingIntervalMs),
                publishingIntervalMs,
                "publishingIntervalMs must be positive.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfNotConnected();
            var sub = new FakeOpcUaSubscription(this);
            _subscriptions.Add(sub);
            return Task.FromResult<IOpcUaSubscription>(sub);
        }
    }

    public ValueTask DisposeAsync()
    {
        FakeOpcUaSubscription[] subs;
        lock (_gate)
        {
            if (_disposed) { return ValueTask.CompletedTask; }
            _disposed = true;
            _connected = false;
            subs = _subscriptions.ToArray();
            _subscriptions.Clear();
        }
        foreach (var s in subs)
        {
            s.MarkDisposedFromClient();
        }
        return ValueTask.CompletedTask;
    }

    // --- Test affordances -------------------------------------------------

    public void SetValue(string nodeId, object value, uint statusCode = 0u)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            var node = GetOrCreate(nodeId);
            node.Value = value;
            node.StatusCode = statusCode;
            node.SourceTimestamp = DateTimeOffset.UtcNow;
        }
    }

    public void SetWriteStatusCode(string nodeId, uint statusCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        lock (_gate)
        {
            var node = GetOrCreate(nodeId);
            node.WriteStatusCode = statusCode;
        }
    }

    internal void RemoveSubscription(FakeOpcUaSubscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private FakeNode GetOrCreate(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            node = new FakeNode();
            _nodes[nodeId] = node;
        }
        return node;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNotConnected()
    {
        if (!_connected)
        {
            throw new InvalidOperationException(
                "FakeOpcUaClient is not connected; call ConnectAsync first.");
        }
    }

    private sealed class FakeNode
    {
        public object? Value { get; set; }
        public uint StatusCode { get; set; }
        public DateTimeOffset SourceTimestamp { get; set; } = DateTimeOffset.UtcNow;
        public uint? WriteStatusCode { get; set; }
    }
}

public sealed record FakeWriteRecord(
    string NodeId,
    object Value,
    OpcUaDataType DataType,
    uint StatusCode);

public sealed class FakeOpcUaSubscription : IOpcUaSubscription
{
    private readonly FakeOpcUaClient _owner;
    private readonly Channel<OpcUaNotification> _channel =
        Channel.CreateUnbounded<OpcUaNotification>();
    private readonly object _gate = new();
    private readonly List<FakeMonitoredItem> _items = new();
    private bool _disposed;

    internal FakeOpcUaSubscription(FakeOpcUaClient owner) { _owner = owner; }

    public IReadOnlyList<FakeMonitoredItem> Items
    {
        get { lock (_gate) { return _items.ToArray(); } }
    }

    public void AddMonitoredItem(string nodeId, OpcUaDataType dataType, int samplingIntervalMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        lock (_gate)
        {
            ThrowIfDisposed();
            _items.Add(new FakeMonitoredItem(nodeId, dataType, samplingIntervalMs));
        }
    }

    public IAsyncEnumerable<OpcUaNotification> NotificationsAsync(
        CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    // Test affordance: push a notification to all NotificationsAsync
    // consumers. Mirrors the real SDK's MonitoredItem-update path.
    public void PushNotification(OpcUaNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            ThrowIfDisposed();
            _channel.Writer.TryWrite(notification);
        }
    }

    public ValueTask DisposeAsync()
    {
        bool justDisposed = false;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                justDisposed = true;
                _channel.Writer.TryComplete();
            }
        }
        if (justDisposed)
        {
            _owner.RemoveSubscription(this);
        }
        return ValueTask.CompletedTask;
    }

    internal void MarkDisposedFromClient()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            _channel.Writer.TryComplete();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

}

public sealed record FakeMonitoredItem(
    string NodeId,
    OpcUaDataType DataType,
    int SamplingIntervalMs);
