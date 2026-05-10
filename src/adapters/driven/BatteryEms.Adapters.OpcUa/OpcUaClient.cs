namespace BatteryEms.Adapters.OpcUa;

// Production wrapper around the OPC Foundation Reference Stack
// (`OPCFoundation.NetStandard.Opc.Ua`). The actual SDK-binding lands
// with **Sub-Slice D** (HIL-Integration) — the implementation needs
// the full SDK lifecycle (ApplicationConfiguration, CertificateValidator,
// SessionFactory, MonitoredItem callbacks, Variant-decoding) which is
// most efficiently designed against an Embedded TestServer in the HIL
// test fixture.
//
// Sub-Slice C ships the type as a build-time stub so the DI extension
// (`OpcUaRegistration.AddBessOpcUa`) has a concrete `IOpcUaClient`
// production-implementation to register without committing to the
// SDK-binding shape today. Tests inject `FakeOpcUaClient`; the host
// path that wires the production stub will throw on the first
// `ConnectAsync`/`ReadAsync`/`WriteAsync` call until Sub-Slice D
// fills in the SDK binding.
//
// Plan-RM-M4-04 §1: M4-04 ist nicht produktiv freigegeben bevor
// RM-M4-05 die Security-Härtung dranhängt. Der Stub-State unterstreicht
// das auf der Implementation-Ebene.
//
// `[ExcludeFromCodeCoverage]` weil jede Methode `NotImplementedException`
// wirft — Coverlet hätte sonst eine permanent-rote Linie auf Code, der
// bewusst noch keinen produktiven Pfad hat.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class OpcUaClient : IOpcUaClient
{
    private readonly OpcUaAdapterOptions _options;

    public OpcUaClient(OpcUaAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public bool IsConnected => false;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException(
            $"OpcUaClient SDK-binding lands with RM-M4-04-D (HIL-Integration). "
            + $"Configured endpoint: {_options.EndpointUrl}. Until Sub-Slice D ships, "
            + "tests should inject FakeOpcUaClient via OpcUaRegistration's overload.");
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<OpcUaReadResult> ReadAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException(
            "OpcUaClient.ReadAsync SDK-binding lands with RM-M4-04-D.");
    }

    public Task<OpcUaWriteResult> WriteAsync(
        string nodeId, object value, OpcUaDataType dataType, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException(
            "OpcUaClient.WriteAsync SDK-binding lands with RM-M4-04-D.");
    }

    public Task<IOpcUaSubscription> CreateSubscriptionAsync(
        int publishingIntervalMs, CancellationToken cancellationToken)
    {
        if (publishingIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publishingIntervalMs), publishingIntervalMs,
                "publishingIntervalMs must be positive.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException(
            "OpcUaClient.CreateSubscriptionAsync SDK-binding lands with RM-M4-04-D.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
