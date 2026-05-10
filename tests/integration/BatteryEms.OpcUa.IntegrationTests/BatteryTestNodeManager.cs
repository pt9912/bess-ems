using Opc.Ua;
using Opc.Ua.Server;

namespace BatteryEms.OpcUa.IntegrationTests;

// Test-only NodeManager that materialises the BatteryEms-Mapping-Knoten
// (`ns=2;s=Battery.Soc`, `Battery.ActivePower`, ...) als
// `BaseDataVariableState`-Instanzen im embedded TestServer. Pro Knoten
// ein scriptbarer Value- und StatusCode-Slot, plus ein Per-Node-
// `OnReadValue`-Hook der das Mapping-StatusCode-Pin aus plan-RM-M4-04
// §4 Sub-Slice D zündet.
//
// Public test affordances:
//   - SetValue(name, value) — mutiert den Variable-Wert; eine vorhandene
//     Subscription pickt den Change auf der nächsten Sampling-Iteration
//     auf (das SDK invalidiert die MonitoredItems automatisch).
//   - SetStatusCode(name, statusCode) — überschreibt den `StatusCode`-
//     Slot des Knotens; Subscribe und Read sehen den gesetzten Code.
//   - GetWrittenValue(name) — liest den letzten clientseitigen
//     Write-Wert für den Setpoint-Roundtrip-Pin.
internal sealed class BatteryTestNodeManager : CustomNodeManager2
{
    public const string NamespaceUri = "urn:bess-ems:test-server";

    private readonly Dictionary<string, BaseDataVariableState> _variablesByName =
        new(StringComparer.Ordinal);
    private readonly object _testGate = new();

    public BatteryTestNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
        : base(server, configuration, NamespaceUri)
    {
    }

    public override NodeId New(ISystemContext context, NodeState node) =>
        // Use the symbolic name as NodeId.Identifier so the wire NodeId
        // matches the `ns=2;s=<browse-name>`-Schema of the simulator
        // mapping JSON.
        new(node.SymbolicName, NamespaceIndexes[0]);

    public void SetValue(string name, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        BaseDataVariableState variable;
        lock (_testGate)
        {
            if (!_variablesByName.TryGetValue(name, out var v))
            {
                throw new InvalidOperationException(
                    $"Unknown test variable '{name}'.");
            }
            variable = v;
        }
        lock (Lock)
        {
            variable.Value = value;
            // Setzt den StatusCode unkonditional zurück auf Good. Ohne
            // dieses Reset würde ein vorheriger SetStatusCode-Aufruf (z.
            // B. aus dem StatusCode-Pin) durchschlagen und einen
            // späteren SetValue-Aufruf in einem anderen Test mit Bad-
            // Status liefern lassen — der OPC-UA-Wire schneidet bei
            // Bad-Status den Value-Block raus, der Adapter sieht null,
            // und der Source rührt die DataQuality auf ProtocolError.
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    public void SetStatusCode(string name, StatusCode statusCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        BaseDataVariableState variable;
        lock (_testGate)
        {
            if (!_variablesByName.TryGetValue(name, out var v))
            {
                throw new InvalidOperationException(
                    $"Unknown test variable '{name}'.");
            }
            variable = v;
        }
        lock (Lock)
        {
            variable.StatusCode = statusCode;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    public object? GetWrittenValue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        BaseDataVariableState variable;
        lock (_testGate)
        {
            if (!_variablesByName.TryGetValue(name, out var v))
            {
                throw new InvalidOperationException(
                    $"Unknown test variable '{name}'.");
            }
            variable = v;
        }
        lock (Lock) { return variable.Value; }
    }

    public override void CreateAddressSpace(
        IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        ArgumentNullException.ThrowIfNull(externalReferences);
        lock (Lock)
        {
            if (!externalReferences.TryGetValue(
                ObjectIds.ObjectsFolder, out var references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }

            var batteryFolder = new FolderState(null)
            {
                SymbolicName = "Battery",
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId("Battery", NamespaceIndexes[0]),
                BrowseName = new QualifiedName("Battery", NamespaceIndexes[0]),
                DisplayName = new LocalizedText("en", "Battery"),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None,
            };
            batteryFolder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(
                ReferenceTypeIds.Organizes, false, batteryFolder.NodeId));
            AddPredefinedNode(SystemContext, batteryFolder);

            var setpointFolder = new FolderState(batteryFolder)
            {
                SymbolicName = "Battery.Setpoint",
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId("Battery.Setpoint", NamespaceIndexes[0]),
                BrowseName = new QualifiedName("Setpoint", NamespaceIndexes[0]),
                DisplayName = new LocalizedText("en", "Setpoint"),
            };
            batteryFolder.AddChild(setpointFolder);
            AddPredefinedNode(SystemContext, setpointFolder);

            CreateVariable(batteryFolder, "Battery.Soc", DataTypeIds.Float, 50.0f);
            CreateVariable(batteryFolder, "Battery.ActivePower", DataTypeIds.Float, 0.0f);
            CreateVariable(batteryFolder, "Battery.ReactivePower", DataTypeIds.Float, 0.0f);
            CreateVariable(batteryFolder, "Battery.Temperature", DataTypeIds.Float, 22.5f);
            CreateVariable(batteryFolder, "Battery.FaultCode", DataTypeIds.UInt16, (ushort)0);
            CreateWritable(setpointFolder, "Battery.Setpoint.ActivePower", DataTypeIds.Float, 0.0f);
            CreateWritable(setpointFolder, "Battery.Setpoint.ReactivePower", DataTypeIds.Float, 0.0f);
        }
    }

    private BaseDataVariableState CreateVariable(
        NodeState parent, string name, NodeId dataType, object initialValue)
    {
        var variable = new BaseDataVariableState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId(name, NamespaceIndexes[0]),
            BrowseName = new QualifiedName(name, NamespaceIndexes[0]),
            DisplayName = new LocalizedText("en", name),
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Historizing = false,
            Value = initialValue,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow,
            MinimumSamplingInterval = MinimumSamplingIntervals.Continuous,
        };
        parent.AddChild(variable);
        AddPredefinedNode(SystemContext, variable);
        lock (_testGate) { _variablesByName[name] = variable; }
        return variable;
    }

    private BaseDataVariableState CreateWritable(
        NodeState parent, string name, NodeId dataType, object initialValue)
    {
        var v = CreateVariable(parent, name, dataType, initialValue);
        v.AccessLevel = AccessLevels.CurrentReadOrWrite;
        v.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
        return v;
    }
}

internal sealed class BatteryTestNodeManagerFactory : INodeManagerFactory
{
    public BatteryTestNodeManager? Manager { get; private set; }

    public StringCollection NamespacesUris =>
        new() { BatteryTestNodeManager.NamespaceUri };

    public INodeManager Create(
        IServerInternal server, ApplicationConfiguration configuration)
    {
        var manager = new BatteryTestNodeManager(server, configuration);
        Manager = manager;
        return manager;
    }
}
