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
// `CertificateValidator` umgesetzt — der Handler-Slot wird in
// `DisposeAsync` wieder unhooked, damit `OpcUaClient`-Instanzen nicht
// einander auf einem geteilten Validator-Handler-Slot belasten.
//
// Cancellation: das SDK honoriert `CancellationToken` historisch nicht
// durchgängig auf langen Reads (siehe plan §7 Risiken). Der Wrapper
// reicht den Token an alle Async-SDK-Aufrufe durch und cap-t den
// Connect-Pfad zusätzlich mit `OpcUaAdapterOptions.ConnectTimeout`,
// damit ein still-stehender Handshake nicht unbeschränkt hängt
// (Review-Fix H1).
public sealed class OpcUaClient : IOpcUaClient
{
    private readonly OpcUaAdapterOptions _options;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<uint, OpcUaSubscription> _subscriptions = new();
    // Per-Instanz-PKI-Verzeichnis. Eindeutig durch GUID, plattform-
    // portabel über `Path.GetTempPath()` (Review-Fix H5). Wird in
    // DisposeAsync best-effort wieder entsorgt.
    private readonly string _pkiRoot = Path.Combine(
        Path.GetTempPath(), "BatteryEms", "OpcUa", "pki",
        $"{Guid.NewGuid():N}");

    private ITelemetryContext? _telemetry;
    private IDisposable? _telemetryDisposable;
    private ApplicationInstance? _application;
    private ApplicationConfiguration? _appConfig;
    private CertificateValidationEventHandler? _certificateAutoAcceptHandler;
    private Session? _session;
    private bool _disposed;

    public OpcUaClient(OpcUaAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public bool IsConnected
    {
        // Best-effort snapshot: `_session.Connected` ist SDK-seitig
        // volatile (Keepalive-Thread setzt das ohne unseren Lock).
        // Der Lock schützt nur die Reference-Zuweisung _session. Das
        // ist die dokumentierte Semantik — siehe Review-Fix L2.
        get { lock (_stateGate) { return _session?.Connected ?? false; } }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is { Connected: true }) { return; }

            // Review-Fix H6 support: stale Session aufräumen, bevor wir
            // eine neue bauen — sonst leakt jeder Mid-Stream-Reconnect
            // eine alte Session-Instanz (samt Keepalive-Thread).
            await DisposeStaleSessionAsync(cancellationToken).ConfigureAwait(false);

            // Review-Fix H1: ConnectTimeout wirklich enforcen.
            using var connectCts = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(_options.ConnectTimeout);
            var token = connectCts.Token;

            await EnsureApplicationConfiguredAsync(token).ConfigureAwait(false);

            var endpointDescription = await CoreClientUtils
                .SelectEndpointAsync(
                    _appConfig!,
                    _options.EndpointUrl.ToString(),
                    useSecurity: false,
                    _telemetry!,
                    token)
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
                    token)
                .ConfigureAwait(false);

            // Review-Fix L3: das SDK unterstützt diesen Setter nach
            // `CreateAsync`-Return; es restartet den Keepalive-Timer mit
            // dem neuen Intervall. Kurzes Fenster mit SDK-Default ist
            // harmlos.
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

    private async Task DisposeStaleSessionAsync(CancellationToken cancellationToken)
    {
        Session? stale;
        lock (_stateGate) { stale = _session; _session = null; }
        if (stale is null) { return; }
        try
        {
            await stale.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Stale-Cleanup ist best-effort.
        catch { }
#pragma warning restore CA1031
        try { stale.Dispose(); }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
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
        // Review-Fix H2: dataType durchsetzen statt silent ignorieren.
        // Mismatch zwischen vorgefertigtem CLR-Typ (Sink boxed bereits
        // typkorrekt) und Mapping-DataType wird hier sichtbar — kein
        // silent-Variant-Default mehr, der dem Operator wie ein
        // Adapter-Bug aussehen würde.
        var variant = BuildVariant(value, dataType);
        var write = new WriteValue
        {
            NodeId = nid,
            AttributeId = Attributes.Value,
            Value = new DataValue { Value = variant },
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
            sdkSubscription.Dispose();
            throw new InvalidOperationException(
                "opcua-subscription-add-failed: session refused AddSubscription.");
        }
        // Review-Fix H3: ohne dieses try/catch leakt eine bereits per
        // `AddSubscription` registrierte Subscription auf dem Server,
        // wenn `CreateAsync` throwed/cancelled — sie ist dann weder
        // im _subscriptions-Tracker noch in unserem Wrapper-Lifecycle.
        try
        {
            await sdkSubscription.CreateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await session.RemoveSubscriptionAsync(sdkSubscription, cancellationToken)
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Best-effort cleanup; rethrow original exception below.
            catch { }
#pragma warning restore CA1031
            sdkSubscription.Dispose();
            throw;
        }

        var wrapper = new OpcUaSubscription(
            sdkSubscription, this, session, _options.SubscriptionChannelCapacity);
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

        // Review-Fix L6: Cert-Validator-Handler in Dispose -=
        // unhooken — der `CertificateValidator` lebt am `_appConfig`,
        // das wir bei einer Sub-Slice-Erweiterung evtl. teilen wollten;
        // heute ist es per-Instanz, aber das aufräumen ist trotzdem
        // korrekt.
        if (_appConfig is not null && _certificateAutoAcceptHandler is not null)
        {
            try
            {
                _appConfig.CertificateValidator.CertificateValidation -=
                    _certificateAutoAcceptHandler;
            }
#pragma warning disable CA1031
            catch { }
#pragma warning restore CA1031
            _certificateAutoAcceptHandler = null;
        }
        _telemetryDisposable?.Dispose();
        _connectGate.Dispose();

        // Review-Fix H5: PKI-Verzeichnis aufräumen, sodass parallele
        // Test-Runs keinen Cert-Pool-Bloat in /tmp anhäufen.
        try
        {
            if (Directory.Exists(_pkiRoot))
            {
                Directory.Delete(_pkiRoot, recursive: true);
            }
        }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
    }

    internal void RemoveSubscription(uint subscriptionId)
        => _subscriptions.TryRemove(subscriptionId, out _);

    // Plan-RM-M4-08 D-05: einziger Test-Hook für die Multi-Cycle-
    // Reconnect-Pin-Linie (`OpcUaNegativeTests`). Liefert nur den
    // Count, nicht das Dictionary selbst — der Wrapper-Typ
    // `OpcUaSubscription` und der Subscription-Id-Schlüssel bleiben
    // private. Tests asserten zwei Invarianten: post-Sample pro Cycle
    // `SubscriptionCount == 1` (Recovery hat alte abgeräumt + neue
    // registriert), post-Dispose `SubscriptionCount == 0`.
    internal int SubscriptionCount => _subscriptions.Count;

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
        // Review-Fix M1/M2: partial-failure-Cleanup. Bauen in lokale
        // Variablen; nur bei Erfolg auf die Felder commiten. Throw vor
        // dem Commit räumt die lokalen Refs ab, sodass Retry sauber
        // neu baut statt Cert-Validator-Handler zu duplizieren oder
        // ApplicationInstance-Zombies zu hinterlassen.
        ITelemetryContext? telemetry = _telemetry;
        IDisposable? telemetryDisposable = _telemetryDisposable;
        if (telemetry is null)
        {
            var defaultTelemetry = DefaultTelemetry.Create(b => { });
            telemetry = defaultTelemetry;
            telemetryDisposable = defaultTelemetry as IDisposable;
        }

        var subject = $"CN={_options.SessionName}, O=BatteryEms, DC=localhost";
        Directory.CreateDirectory(_pkiRoot);
        var certs = ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
            subject, CertificateStoreType.Directory, _pkiRoot);

        var application = new ApplicationInstance(telemetry)
        {
            ApplicationName = _options.SessionName,
            ApplicationType = ApplicationType.Client,
        };

        try
        {
            await application
                .Build(
                    applicationUri: $"urn:bess-ems:opcua-client:{_options.SessionName}",
                    productUri: "urn:bess-ems:opcua-client")
                .AsClient()
                .AddSecurityConfiguration(certs, _pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);

            await application
                .CheckApplicationInstanceCertificatesAsync(
                    silent: true,
                    CertificateFactory.DefaultLifeTime,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Wenn wir die Telemetry hier just-in-time gebaut haben
            // (war vorher null), Dispose'n wir sie auch hier — das
            // OpcUaClient.DisposeAsync würde sie sonst nicht sehen.
            if (_telemetry is null)
            {
                telemetryDisposable?.Dispose();
            }
            throw;
        }

        var appConfig = application.ApplicationConfiguration;
        // Named handler statt Lambda, damit Dispose -= denselben
        // Handler-Slot trifft (Review-Fix L6 + M1).
        var handler = new CertificateValidationEventHandler(
            (sender, e) => e.Accept = true);
        appConfig.CertificateValidator.CertificateValidation += handler;

        // Atomic commit: nur jetzt, nach allen await-Punkten ohne
        // Throw, übernehmen wir die lokalen Werte in die Felder.
        _telemetry = telemetry;
        _telemetryDisposable = telemetryDisposable;
        _application = application;
        _appConfig = appConfig;
        _certificateAutoAcceptHandler = handler;
    }

    // Review-Fix H2: typsicheres Variant-Build. Sink boxed bereits per
    // `OpcUaCommandSink.TryBoxForDataType` auf den passenden CLR-Typ;
    // wir verifizieren, dass das CLR-Boxing zum Mapping-DataType passt.
    // Mismatch ist ein Operator-/Code-Bug, kein Wire-Fehler — Throw mit
    // klarem Reason, damit das nicht silent als Bad-StatusCode auftaucht.
    private static Variant BuildVariant(object value, OpcUaDataType dataType)
    {
        switch (dataType)
        {
            case OpcUaDataType.Bool when value is bool b:
                return new Variant(b);
            case OpcUaDataType.Int16 when value is short s:
                return new Variant(s);
            case OpcUaDataType.Int32 when value is int i:
                return new Variant(i);
            case OpcUaDataType.Int64 when value is long l:
                return new Variant(l);
            case OpcUaDataType.UInt16 when value is ushort us:
                return new Variant(us);
            case OpcUaDataType.UInt32 when value is uint ui:
                return new Variant(ui);
            case OpcUaDataType.UInt64 when value is ulong ul:
                return new Variant(ul);
            case OpcUaDataType.Float when value is float f:
                return new Variant(f);
            case OpcUaDataType.Double when value is double d:
                return new Variant(d);
            case OpcUaDataType.String when value is string str:
                return new Variant(str);
            default:
                throw new ArgumentException(
                    $"opcua-client-type-mismatch: dataType={dataType} requires a "
                    + $"matching CLR type, got {value.GetType().Name}. "
                    + "Caller (e.g. OpcUaCommandSink) must box the value before "
                    + "calling WriteAsync.",
                    nameof(value));
        }
    }
}

// IOpcUaSubscription wrapper around `Opc.Ua.Client.Subscription`. Maps
// SDK MonitoredItem-Notifications onto our async-enumerable contract.
internal sealed class OpcUaSubscription : IOpcUaSubscription
{
    private readonly Subscription _sdkSubscription;
    private readonly OpcUaClient _owner;
    private readonly Session _session;
    // Plan-RM-M4-08-A Bug-Fix: die Subscription-Id wird beim Add im
    // Konstruktor geschnappt und intern gehalten. Der OPC-Foundation-
    // SDK setzt `Subscription.Id` nach `DeleteAsync(silent: true)`
    // u. U. zurück — Mitstreiter wäre dann die Map-Entry mit der
    // ursprünglichen Id verwaist. Cached-Id macht
    // `_owner.RemoveSubscription(...)` deterministisch.
    private readonly uint _subscriptionId;
    private readonly Channel<OpcUaNotification> _channel;
    private int _disposed;

    public OpcUaSubscription(
        Subscription sdkSubscription,
        OpcUaClient owner,
        Session session,
        int channelCapacity)
    {
        _sdkSubscription = sdkSubscription;
        _owner = owner;
        _session = session;
        _subscriptionId = sdkSubscription.Id;
        // Review-Fix M4: bounded Channel mit DropOldest, Kapazität aus
        // den OpcUaAdapterOptions. Der frühere unbounded Channel hier
        // hätte die D-03-Backpressure-Garantie der Source unterlaufen
        // (Source-Bounded-Channel hätte nie Pressure gesehen, weil
        // dieser hier erst alles gepuffert hätte).
        _channel = Channel.CreateBounded<OpcUaNotification>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    // Review-Fix H4: async + CT — der Apply ist eine echte Server-
    // Round-Trip-Operation (`CreateMonitoredItems`-Service). Sync-over-
    // async hier hat ohnehin nur funktioniert, weil alle Caller
    // ThreadPool-driven sind.
    public async Task AddMonitoredItemAsync(
        string nodeId, OpcUaDataType dataType, int samplingIntervalMs,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        // Review-Fix H2: dataType wird hier (noch) nicht in Variant-
        // Decoding genutzt — die Source-Seite mappt SDK-Variant über
        // OpcUaDataTypeParser.TryToDouble, der den CLR-Wert direkt
        // liest. Wir parken ihn als Diagnose-Hilfe für ein zukünftiges
        // Per-Item-DecodeFilter (F-15 Type-System-Erweiterung); aktuell
        // ist die Funktion ein No-op-Touch.
        _ = dataType;
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
        await _sdkSubscription.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<OpcUaNotification> NotificationsAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        // Review-Fix M3: Volatile-Compare-Exchange — _disposed wird von
        // mehreren Threads (Source-Pump, Source-Dispose, Owner-Dispose)
        // geschrieben/gelesen.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        _channel.Writer.TryComplete();
        try
        {
            await _sdkSubscription.DeleteAsync(silent: true).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Adapter boundary — Dispose must not throw.
        catch { }
#pragma warning restore CA1031
        // Review-Fix L5: Subscription auch von der Session entkoppeln,
        // damit langlebige Sessions (z.B. Mid-Stream-Recovery, F-13
        // Multi-Server) keinen wachsenden Subscription-Slot-Pool tragen.
        try
        {
            await _session.RemoveSubscriptionAsync(_sdkSubscription, default)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
        _owner.RemoveSubscription(_subscriptionId);
        _sdkSubscription.Dispose();
    }
}
