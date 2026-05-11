using System.Runtime.CompilerServices;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OpcUa;

// RM-M4-04-B: OPC-UA-Telemetry-Source. Implementiert
// `IBatteryTelemetrySource` per plan-RM-M4-04 §4 Sub-Slice B. Der
// Source pumpt Subscribe-Notifications über einen
// `OverflowAwareTelemetryChannel` von der SDK-Callback-Seite zur
// Consumer-Seite und sammelt zusätzlich pro Tick die `direction=read`-
// Knoten. Die latente Sample-Cache (`OpcUaTelemetryAssembler`) hält
// die letzten Werte pro Mapping-`name` und produziert pro Tick ein
// `BatteryTelemetry`-Sample mit Worst-of-`DataQuality`.
//
// Lifecycle (D-09): IAsyncDisposable. DisposeAsync cancelt den
// internen CTS, completet den Channel-Writer (post-Dispose-
// SDK-Callbacks treffen einen completed-Channel und werden silent
// gedropt), gibt die Subscription frei und ruft `IOpcUaClient.
// DisposeAsync`.
//
// Status.Connected (D-09 Klärung): aktive Session AND (kein Subscribe-
// Knoten im Mapping ODER aktive Subscription). Bei rein read-only
// Mappings gibt es keine Subscription, und der Source darf nicht
// fälschlich als Disconnected gemeldet werden.
//
// **Bekannte Lücke (M4-04-B-Scope-Stop):** Mid-Stream-Subscription-
// Tod wird nicht aktiv erkannt oder repariert. Wenn der Server die
// Subscription kappt (Session noch aktiv), endet der Pump-Task
// silent; `Status.Connected` bleibt true (Session ist intakt) während
// neue Subscribe-Werte nicht mehr fließen. Read-direction-Knoten
// funktionieren weiter; der DataQuality-Pfad bleibt für die nicht
// mehr aktualisierten Subscribe-Knoten am letzten gespeicherten Wert
// hängen. Eine echte Recovery-Schleife (Subscription neu anlegen,
// MonitoredItems re-attachen) ist explizit Sub-Slice-D-HIL-Test-
// Trigger oder Folgearbeit (analog zur F-13 Multi-Server-Linie). Der
// Modbus-Adapter delegiert die Recovery an den FluentModbusClient
// und hat dieselbe Semantik auf der Source-Schicht; OPC-UA wird
// vergleichbar wenn der OPC-Foundation-SDK-Wrapper später eine
// Subscription-Lifecycle-Notification anbietet.
public sealed class OpcUaTelemetrySource : IBatteryTelemetrySource, IAsyncDisposable
{
    private readonly IOpcUaClient _client;
    private readonly OpcUaMappingConfiguration _mapping;
    private readonly OpcUaAdapterOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<OpcUaTelemetrySource> _logger;
    private readonly OpcUaTelemetryAssembler _assembler;
    private readonly OverflowAwareTelemetryChannel _notifications;
    private readonly IReadOnlyList<NodeBinding> _readNodes;
    private readonly IReadOnlyList<NodeBinding> _subscribeNodes;
    private readonly bool _hasSubscribeNodes;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _stateGate = new();

    private AdapterStatus _status = AdapterStatus.Disconnected;
    private IOpcUaSubscription? _subscription;
    private Task? _notificationPump;
    private bool _disposed;

    public OpcUaTelemetrySource(
        IOpcUaClient client,
        OpcUaMappingConfiguration mapping,
        OpcUaAdapterOptions options,
        BatteryAsset asset,
        IClock clock,
        ILogger<OpcUaTelemetrySource> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        // D-04 Security-Startup-Guard fires here if the operator has
        // not opted in to unsecured operation.
        options.EnsureValid(logger);

        _client = client;
        _mapping = mapping;
        _options = options;
        _clock = clock;
        _logger = logger;
        _assembler = new OpcUaTelemetryAssembler(asset.AssetId);
        _notifications = new OverflowAwareTelemetryChannel(options.SubscriptionChannelCapacity);

        var read = new List<NodeBinding>();
        var subscribe = new List<NodeBinding>();
        foreach (var node in mapping.Nodes)
        {
            // Skip writable nodes — they belong to the OpcUaCommandSink.
            if (node.Direction.Equals("write", StringComparison.OrdinalIgnoreCase) || node.Writable)
            {
                continue;
            }
            // Defensive structural checks (the JSON-loader normally
            // rejects these via the schema, but programmatic /
            // alternative-loader paths could slip them through; better
            // to throw at construction than emit telemetry with
            // empty NodeId).
            if (string.IsNullOrWhiteSpace(node.Name))
            {
                throw new ArgumentException(
                    "OPC-UA mapping node has a null/empty 'name'.", nameof(mapping));
            }
            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                throw new ArgumentException(
                    $"OPC-UA mapping node '{node.Name}' has a null/empty 'node_id'.",
                    nameof(mapping));
            }
            if (!double.IsFinite(node.ScaleFactor) || node.ScaleFactor == 0.0)
            {
                throw new ArgumentException(
                    $"OPC-UA mapping node '{node.Name}' has a non-finite or zero scale_factor "
                    + $"({node.ScaleFactor}); the JSON schema rejects scale_factor=0 at the loader, "
                    + "but a programmatic mapping could slip it through and trigger a divide-by-zero "
                    + "in the Sub-Slice-C command sink.", nameof(mapping));
            }
            var dataType = OpcUaDataTypeParser.Parse(node.DataType);
            var binding = new NodeBinding(
                Name: node.Name,
                NodeId: node.NodeId,
                DataType: dataType,
                ScaleFactor: node.ScaleFactor,
                SamplingIntervalMs: node.MonitoringIntervalMs ?? options.DefaultMonitoringIntervalMs);
            if (node.Direction.Equals("subscribe", StringComparison.OrdinalIgnoreCase))
            {
                subscribe.Add(binding);
            }
            else if (node.Direction.Equals("read", StringComparison.OrdinalIgnoreCase))
            {
                read.Add(binding);
            }
            else
            {
                throw new ArgumentException(
                    $"OPC-UA mapping node '{node.Name}' has unsupported direction '{node.Direction}'. "
                    + "Allowed: read, subscribe, write.",
                    nameof(mapping));
            }
        }
        _readNodes = read;
        _subscribeNodes = subscribe;
        _hasSubscribeNodes = subscribe.Count > 0;
    }

    public AdapterStatus Status
    {
        get { lock (_stateGate) { return _status; } }
    }

    public async IAsyncEnumerable<BatteryTelemetry> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _shutdownCts.Token);
        var token = linkedCts.Token;

        await ConnectWithBackoffAsync(token).ConfigureAwait(false);
        await EnsureSubscriptionAsync(token).ConfigureAwait(false);
        UpdateStatusOnSuccess();

        while (!token.IsCancellationRequested)
        {
            var sample = await TryReadOnceAsync(token).ConfigureAwait(false);
            if (sample is not null)
            {
                yield return sample;
            }

            // Review-Fix H6: Mid-Stream-Reconnect. Recovery zündet wenn
            // (a) die TCP-Schicht den Disconnect schon erkannt hat
            // (`!_client.IsConnected`), oder (b) wir wiederholt
            // Read-Fehler sehen. (b) ist nötig weil der SDK-
            // `Session.Connected` über den Keepalive-Timer aktualisiert
            // wird und nach einem Server-Stop bis zu `KeepAliveInterval`
            // hinterherhinkt — währenddessen würden Reads silently
            // throwen ohne dass die Health-Statusmaschine recovery
            // anfordert.
            if (!token.IsCancellationRequested
                && !_disposed
                && (!_client.IsConnected || ShouldRecoverAfterFailures()))
            {
                await RecoverConnectionAsync(token).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(_options.PollingInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private bool ShouldRecoverAfterFailures()
    {
        // Schwelle: zwei aufeinanderfolgende Read-Failures sind
        // typischerweise schon ein dead-session-Signal (transient-
        // single-failure-Fenster bleibt unangetastet). Master-DoD
        // Reconnect-Schleife wird damit zuverlässig getriggert,
        // ohne dass jeder Einmal-Fehler eine teure Recovery zündet.
        lock (_stateGate)
        {
            return _status.ConsecutiveFailures >= 2;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Recovery is best-effort: any exception is surfaced via AdapterStatus and re-tried on the next tick.")]
    private async Task RecoverConnectionAsync(CancellationToken cancellationToken)
    {
        OpcUaTelemetrySourceLog.LogReconnectAttempt(_logger);
        Task? oldPump;
        IOpcUaSubscription? oldSub;
        lock (_stateGate)
        {
            oldPump = _notificationPump;
            oldSub = _subscription;
            _notificationPump = null;
            _subscription = null;
        }
        if (oldSub is not null)
        {
            try { await oldSub.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                OpcUaTelemetrySourceLog.LogSubscriptionDisposeFailure(_logger, ex);
            }
        }
        if (oldPump is not null)
        {
            // Old pump exits on its own when the disposed subscription's
            // channel completes — but we wait for it (briefly) so the
            // new pump doesn't race with it on the shared overflow
            // channel.
            try { await oldPump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OpcUaTelemetrySourceLog.LogPumpDisposeFailure(_logger, ex);
            }
        }

        try
        {
            // Force-Disconnect bevor Reconnect: ohne diesen Schritt
            // würde `OpcUaClient.ConnectAsync` early-return-en, wenn der
            // SDK-`Session.Connected` noch true ist (Keepalive-Timer
            // hat den TCP-Drop noch nicht gemerkt). Wir wissen aus der
            // Failure-Schwelle, dass die Session de-facto tot ist.
            try
            {
                using var disconnectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _client.DisconnectAsync(disconnectCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OpcUaTelemetrySourceLog.LogClientDisposeFailure(_logger, ex);
            }
            await ConnectWithBackoffAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            UpdateStatusOnSuccess();
            OpcUaTelemetrySourceLog.LogReconnectSuccess(_logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdateStatusOnFailure(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? pump;
        IOpcUaSubscription? sub;
        lock (_stateGate)
        {
            if (_disposed) { return; }
            _disposed = true;
            pump = _notificationPump;
            sub = _subscription;
            _subscription = null;
            _notificationPump = null;
        }
        await _shutdownCts.CancelAsync().ConfigureAwait(false);
        _notifications.Complete();
        if (pump is not null)
        {
            try { await pump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
#pragma warning disable CA1031 // Adapter boundary — Dispose must not throw.
            catch (Exception ex)
            {
                OpcUaTelemetrySourceLog.LogPumpDisposeFailure(_logger, ex);
            }
#pragma warning restore CA1031
        }
        if (sub is not null)
        {
            try { await sub.DisposeAsync().ConfigureAwait(false); }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                OpcUaTelemetrySourceLog.LogSubscriptionDisposeFailure(_logger, ex);
            }
#pragma warning restore CA1031
        }
        try { await _client.DisposeAsync().ConfigureAwait(false); }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            OpcUaTelemetrySourceLog.LogClientDisposeFailure(_logger, ex);
        }
#pragma warning restore CA1031
        // _shutdownCts.Dispose() runs after Cancel + after every
        // awaited consumer has yielded. The linked-CTS in ReadAsync
        // (CreateLinkedTokenSource(externalToken, _shutdownCts.Token))
        // is `using`-disposed by the consumer's caller scope, so the
        // registration on this CTS is unhooked before this Dispose.
        // Reading from a token of a disposed CTS is documented as
        // safe; only `Cancel` would throw, which is no longer needed.
        _shutdownCts.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Adapter boundary: arbitrary protocol exceptions are surfaced via AdapterStatus so the worker degrades gracefully (analog zur Modbus-Linie).")]
    private async Task<BatteryTelemetry?> TryReadOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Capture overflow BEFORE drain. DrainSubscribeNotifications
            // clears the flag unconditionally (plan §148); if we read
            // HasOverflow after the drain, every backlog tick would
            // emit Valid samples and the Stale floor would never reach
            // the consumer. Reading pre-drain pins the floor to the
            // backlog state that produced this tick's samples.
            var floor = _notifications.HasOverflow
                ? DataQuality.Stale("opcua-subscription-overflow")
                : null;

            // Drain any pending subscribe notifications first so per-
            // tick read values overlay them.
            DrainSubscribeNotifications();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ReadTimeout);
            foreach (var node in _readNodes)
            {
                var result = await _client.ReadAsync(node.NodeId, cts.Token).ConfigureAwait(false);
                ApplyResult(node, result.Value, result.StatusCode);
            }
            if (!_assembler.HasAnyEntry)
            {
                // Nothing to emit yet on a brand-new mapping with only
                // subscribe nodes that haven't pushed anything.
                return null;
            }
            var now = _clock.UtcNow;
            UpdateStatusOnSuccess(now);
            return _assembler.Build(now, floor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdateStatusOnFailure(ex);
            return null;
        }
    }

    private void DrainSubscribeNotifications()
    {
        foreach (var notification in _notifications.DrainAll())
        {
            var binding = FindBinding(notification.NodeId);
            if (binding is null) { continue; }
            ApplyResult(binding, notification.Value, notification.StatusCode);
        }
    }

    private NodeBinding? FindBinding(string nodeId)
    {
        foreach (var node in _subscribeNodes)
        {
            if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal)) { return node; }
        }
        foreach (var node in _readNodes)
        {
            if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal)) { return node; }
        }
        return null;
    }

    private void ApplyResult(NodeBinding node, object? value, uint statusCode)
    {
        var quality = OpcUaStatusCodeMapper.Map(statusCode);
        if (!OpcUaDataTypeParser.TryToDouble(value, out var raw))
        {
            // Bad/Uncertain StatusCodes häufig mit Null-Value ankommen;
            // der Server lässt die Value-Bytes weg wenn die Severity-Bits
            // gesetzt sind (LH-OPCUA-004). In dem Fall ist der echte
            // Fehler die Severity, nicht das Type-Mismatch — also nicht
            // überschreiben. Nur wenn StatusCode=Good ist und der Wert
            // trotzdem nicht parsebar, surfaced wir das als
            // `opcua-type-mismatch` (echter Mapping-Bug auf Operator-
            // Seite — Mapping sagt z. B. Float, Server liefert String).
            if (quality.Flag == DataQualityState.Valid)
            {
                quality = DataQuality.ProtocolError("opcua-type-mismatch");
            }
            raw = 0;
        }
        var scaled = raw * node.ScaleFactor;
        _assembler.Update(node.Name, scaled, quality);
    }

    private async Task ConnectWithBackoffAsync(CancellationToken cancellationToken)
    {
        var delay = _options.ReconnectBackoffStart;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Connect-failure-classification ist Adapter-Konzern; transienter Fehler triggert Backoff.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                OpcUaTelemetrySourceLog.LogConnectFailure(_logger, ex, delay);
                UpdateStatusOnFailure(ex);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                delay = delay + delay > _options.ReconnectBackoffMax
                    ? _options.ReconnectBackoffMax
                    : delay + delay;
            }
        }
    }

    private async Task EnsureSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (!_hasSubscribeNodes) { return; }
        var subscription = await _client
            .CreateSubscriptionAsync(_options.DefaultMonitoringIntervalMs, cancellationToken)
            .ConfigureAwait(false);
        foreach (var node in _subscribeNodes)
        {
            await subscription
                .AddMonitoredItemAsync(
                    node.NodeId, node.DataType, node.SamplingIntervalMs, cancellationToken)
                .ConfigureAwait(false);
        }

        // If DisposeAsync ran between CreateSubscriptionAsync and this
        // point, the freshly created server-side subscription would
        // leak unless we dispose it explicitly. The lock answers
        // *whether* we own it; the actual disposal happens out-of-
        // lock to avoid awaiting under a sync lock.
        bool disposedRace;
        Task? pump = null;
        lock (_stateGate)
        {
            disposedRace = _disposed;
            if (!disposedRace)
            {
                _subscription = subscription;
                pump = Task.Run(
                    () => PumpNotificationsAsync(subscription, _shutdownCts.Token),
                    _shutdownCts.Token);
                _notificationPump = pump;
            }
        }

        if (disposedRace)
        {
            try { await subscription.DisposeAsync().ConfigureAwait(false); }
#pragma warning disable CA1031 // best-effort cleanup on the dispose-race path
            catch (Exception ex)
            {
                OpcUaTelemetrySourceLog.LogSubscriptionDisposeFailure(_logger, ex);
            }
#pragma warning restore CA1031
            throw new ObjectDisposedException(nameof(OpcUaTelemetrySource));
        }
        // Touch `pump` to silence the "assigned but not used" hint
        // in case future refactors split this further.
        _ = pump;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Background pump must not crash on transient subscription errors; the source's outer loop reads AdapterStatus and degrades.")]
    private async Task PumpNotificationsAsync(
        IOpcUaSubscription subscription,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var notification in subscription
                .NotificationsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) { break; }
                if (_disposed) { break; }
                _notifications.TryWrite(notification);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            OpcUaTelemetrySourceLog.LogSubscriptionPumpError(_logger, ex);
        }
    }

    private void UpdateStatusOnSuccess(DateTimeOffset? at = null)
    {
        var now = at ?? _clock.UtcNow;
        lock (_stateGate)
        {
            _status = new AdapterStatus(
                Connected: _client.IsConnected
                    && (!_hasSubscribeNodes || _subscription is not null),
                LastSuccessfulReadAt: now,
                LastError: null,
                ConsecutiveFailures: 0);
        }
    }

    private void UpdateStatusOnFailure(Exception ex)
    {
        lock (_stateGate)
        {
            _status = new AdapterStatus(
                Connected: _client.IsConnected
                    && (!_hasSubscribeNodes || _subscription is not null),
                LastSuccessfulReadAt: _status.LastSuccessfulReadAt,
                LastError: ex.Message,
                ConsecutiveFailures: _status.ConsecutiveFailures + 1);
        }
    }

    private sealed record NodeBinding(
        string Name,
        string NodeId,
        OpcUaDataType DataType,
        double ScaleFactor,
        int SamplingIntervalMs);
}

internal static partial class OpcUaTelemetrySourceLog
{
    [LoggerMessage(EventId = 4210, Level = LogLevel.Warning,
        Message = "opcua connect failed; retrying after {Delay}.")]
    public static partial void LogConnectFailure(
        ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(EventId = 4211, Level = LogLevel.Error,
        Message = "opcua subscription pump terminated with an error.")]
    public static partial void LogSubscriptionPumpError(
        ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4212, Level = LogLevel.Warning,
        Message = "opcua subscription pump task threw on dispose.")]
    public static partial void LogPumpDisposeFailure(
        ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4213, Level = LogLevel.Warning,
        Message = "opcua subscription DisposeAsync threw.")]
    public static partial void LogSubscriptionDisposeFailure(
        ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4214, Level = LogLevel.Warning,
        Message = "opcua client DisposeAsync threw.")]
    public static partial void LogClientDisposeFailure(
        ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4215, Level = LogLevel.Information,
        Message = "opcua mid-stream reconnect attempt: session lost, rebuilding.")]
    public static partial void LogReconnectAttempt(ILogger logger);

    [LoggerMessage(EventId = 4216, Level = LogLevel.Information,
        Message = "opcua mid-stream reconnect succeeded.")]
    public static partial void LogReconnectSuccess(ILogger logger);
}
