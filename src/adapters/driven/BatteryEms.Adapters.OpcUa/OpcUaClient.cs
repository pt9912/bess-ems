using System.Collections.Concurrent;
using System.Threading.Channels;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace BatteryEms.Adapters.OpcUa;

// Production wrapper around the OPC Foundation Reference Stack
// (`OPCFoundation.NetStandard.Opc.Ua` 1.5.378.x → MIT-lizenziert per
// plan-RM-M4-04 D-01). Implementiert den `IOpcUaClient`-Driven-Port
// gegen `Opc.Ua.Client.Session`. Sub-Slice D Lieferung — der vorherige
// `NotImplementedException`-Stub aus Sub-Slice C ist hiermit ersetzt.
//
// Security-Profil: M4-04 läuft mit `MessageSecurityMode.None` plus dem
// AllowUnsecured-Startup-Guard auf der bool-Achse (D-04). M4-05 layert
// die RuntimeProfile-Awareness drauf. Hier wird das per
// `useSecurity=false`-Endpoint-Selection und einem auto-akzeptierenden
// `CertificateValidator` umgesetzt.
//
// Cancellation: das SDK honoriert `CancellationToken` historisch nicht
// durchgängig auf langen Reads (siehe plan §7 Risiken). Der Wrapper
// reicht den Token an alle Async-SDK-Aufrufe durch und setzt den
// Session-`KeepAliveInterval` aus Options, damit die Session nicht
// silent stale wird.
public sealed class OpcUaClient : IOpcUaClient
{
    private readonly OpcUaAdapterOptions _options;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<uint, OpcUaSubscription> _subscriptions = new();

    private ITelemetryContext? _telemetry;
    private IDisposable? _telemetryDisposable;
    private ApplicationInstance? _application;
    private ApplicationConfiguration? _appConfig;
    private Session? _session;
    private bool _disposed;

    public OpcUaClient(OpcUaAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public bool IsConnected
    {
        get { lock (_stateGate) { return _session is not null && _session.Connected; } }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is { Connected: true }) { return; }

            await EnsureApplicationConfiguredAsync(cancellationToken).ConfigureAwait(false);

            var endpointDescription = await CoreClientUtils
                .SelectEndpointAsync(
                    _appConfig!,
                    _options.EndpointUrl.ToString(),
                    useSecurity: false,
                    _telemetry!,
                    cancellationToken)
                .ConfigureAwait(false);
            var endpointConfiguration = EndpointConfiguration.Create(_appConfig!);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            var sessionFactory = new DefaultSessionFactory(_telemetry!);
            var session = (Session)await sessionFactory
                .CreateAsync(
                    _appConfig!,
                    endpoint,
                    updateBeforeConnect: false,
                    _options.SessionName,
                    sessionTimeout: 60_000,
                    identity: null,
                    preferredLocales: default,
                    cancellationToken)
                .ConfigureAwait(false);

            session.KeepAliveInterval = (int)_options.KeepAliveInterval.TotalMilliseconds;
            lock (_stateGate) { _session = session; }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Session? toClose;
        lock (_stateGate) { toClose = _session; _session = null; }
        if (toClose is null) { return; }
        try
        {
            await toClose.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            toClose.Dispose();
        }
    }

    public async Task<OpcUaReadResult> ReadAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var session = RequireSession();
        var nid = NodeId.Parse(nodeId);
        var dv = await session.ReadValueAsync(nid, cancellationToken).ConfigureAwait(false);
        return new OpcUaReadResult(
            NodeId: nodeId,
            Value: dv.Value,
            StatusCode: dv.StatusCode.Code,
            SourceTimestamp: new DateTimeOffset(
                DateTime.SpecifyKind(dv.SourceTimestamp, DateTimeKind.Utc), TimeSpan.Zero));
    }

    public async Task<OpcUaWriteResult> WriteAsync(
        string nodeId, object value, OpcUaDataType dataType, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var session = RequireSession();
        var nid = NodeId.Parse(nodeId);
        var write = new WriteValue
        {
            NodeId = nid,
            AttributeId = Attributes.Value,
            Value = new DataValue { Value = new Variant(value) },
        };
        var request = new WriteValueCollection { write };
        var response = await session.WriteAsync(null, request, cancellationToken)
            .ConfigureAwait(false);
        var statusCode = response.Results.Count > 0
            ? response.Results[0].Code
            : StatusCodes.BadInternalError;
        return new OpcUaWriteResult(nodeId, statusCode);
    }

    public async Task<IOpcUaSubscription> CreateSubscriptionAsync(
        int publishingIntervalMs, CancellationToken cancellationToken)
    {
        if (publishingIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publishingIntervalMs), publishingIntervalMs,
                "publishingIntervalMs must be positive.");
        }
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var session = RequireSession();

        var sdkSubscription = new Subscription(session.DefaultSubscription)
        {
            PublishingInterval = publishingIntervalMs,
            PublishingEnabled = true,
        };
        if (!session.AddSubscription(sdkSubscription))
        {
            throw new InvalidOperationException(
                "opcua-subscription-add-failed: session refused AddSubscription.");
        }
        await sdkSubscription.CreateAsync(cancellationToken).ConfigureAwait(false);

        var wrapper = new OpcUaSubscription(sdkSubscription, this);
        _subscriptions.TryAdd(sdkSubscription.Id, wrapper);
        return wrapper;
    }

    public async ValueTask DisposeAsync()
    {
        bool wasDisposed;
        lock (_stateGate) { wasDisposed = _disposed; _disposed = true; }
        if (wasDisposed) { return; }
        foreach (var sub in _subscriptions.Values)
        {
            try { await sub.DisposeAsync().ConfigureAwait(false); }
#pragma warning disable CA1031 // Adapter boundary — Dispose must not throw.
            catch { }
#pragma warning restore CA1031
        }
        _subscriptions.Clear();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await DisconnectAsync(cts.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
        _telemetryDisposable?.Dispose();
        _connectGate.Dispose();
    }

    internal void RemoveSubscription(uint subscriptionId)
        => _subscriptions.TryRemove(subscriptionId, out _);

    internal Session RequireSession()
    {
        Session? session;
        lock (_stateGate) { session = _session; }
        if (session is null || !session.Connected)
        {
            throw new InvalidOperationException(
                "opcua-client-not-connected: call ConnectAsync first.");
        }
        return session;
    }

    private async Task EnsureApplicationConfiguredAsync(CancellationToken cancellationToken)
    {
        if (_appConfig is not null) { return; }
        if (_telemetry is null)
        {
            var defaultTelemetry = DefaultTelemetry.Create(b => { });
            _telemetry = defaultTelemetry;
            _telemetryDisposable = defaultTelemetry as IDisposable;
        }

        var subject = $"CN={_options.SessionName}, O=BatteryEms, DC=localhost";
        var pkiRoot = "%TEMP%/BatteryEms/OpcUa/pki";
        var certs = ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
            subject, CertificateStoreType.Directory, pkiRoot);

        _application = new ApplicationInstance(_telemetry)
        {
            ApplicationName = _options.SessionName,
            ApplicationType = ApplicationType.Client,
        };

        await _application
            .Build(
                applicationUri: $"urn:bess-ems:opcua-client:{_options.SessionName}",
                productUri: "urn:bess-ems:opcua-client")
            .AsClient()
            .AddSecurityConfiguration(certs, pkiRoot)
            .SetAutoAcceptUntrustedCertificates(true)
            .CreateAsync(cancellationToken)
            .ConfigureAwait(false);

        await _application
            .CheckApplicationInstanceCertificatesAsync(
                silent: true,
                CertificateFactory.DefaultLifeTime,
                cancellationToken)
            .ConfigureAwait(false);

        _appConfig = _application.ApplicationConfiguration;
        _appConfig.CertificateValidator.CertificateValidation +=
            (sender, e) => e.Accept = true;
    }
}

// IOpcUaSubscription wrapper around `Opc.Ua.Client.Subscription`. Maps
// SDK MonitoredItem-Notifications onto our async-enumerable contract.
internal sealed class OpcUaSubscription : IOpcUaSubscription
{
    private readonly Subscription _sdkSubscription;
    private readonly OpcUaClient _owner;
    private readonly Channel<OpcUaNotification> _channel =
        Channel.CreateUnbounded<OpcUaNotification>();
    private bool _disposed;

    public OpcUaSubscription(Subscription sdkSubscription, OpcUaClient owner)
    {
        _sdkSubscription = sdkSubscription;
        _owner = owner;
    }

    public void AddMonitoredItem(string nodeId, OpcUaDataType dataType, int samplingIntervalMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = new MonitoredItem(_sdkSubscription.DefaultItem)
        {
            StartNodeId = NodeId.Parse(nodeId),
            AttributeId = Attributes.Value,
            SamplingInterval = samplingIntervalMs,
            QueueSize = 1,
            DiscardOldest = true,
        };
        item.Notification += (mi, e) =>
        {
            foreach (var dv in mi.DequeueValues())
            {
                _channel.Writer.TryWrite(new OpcUaNotification(
                    NodeId: nodeId,
                    Value: dv.Value,
                    StatusCode: dv.StatusCode.Code,
                    SourceTimestamp: new DateTimeOffset(
                        DateTime.SpecifyKind(dv.SourceTimestamp, DateTimeKind.Utc), TimeSpan.Zero)));
            }
        };
        _sdkSubscription.AddItem(item);
        // ApplyChangesAsync bei jedem Add ist konservativ — das SDK
        // publisht den MonitoredItem damit ohne explizite Apply-Phase
        // im Sub-Slice-B-Read-Loop. Token-less Aufruf weil das hier
        // keine Cancellation-Erwartung hat (synchroner SDK-Pfad
        // hinter der Async-Fassade).
        _sdkSubscription.ApplyChangesAsync(default).GetAwaiter().GetResult();
    }

    public IAsyncEnumerable<OpcUaNotification> NotificationsAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        _channel.Writer.TryComplete();
        try
        {
            await _sdkSubscription.DeleteAsync(silent: true).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Adapter boundary — Dispose must not throw.
        catch { }
#pragma warning restore CA1031
        _owner.RemoveSubscription(_sdkSubscription.Id);
        _sdkSubscription.Dispose();
    }
}
