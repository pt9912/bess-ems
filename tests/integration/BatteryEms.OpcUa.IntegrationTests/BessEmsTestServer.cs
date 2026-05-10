using Opc.Ua;
using Opc.Ua.Server;

namespace BatteryEms.OpcUa.IntegrationTests;

// Test-only StandardServer subclass. Wires den BatteryTestNodeManager
// per `CreateMasterNodeManager`-Override; die ApplicationInstance
// reicht das ServerInternal hier durch.
internal sealed class BessEmsTestServer : StandardServer
{
    private readonly BatteryTestNodeManagerFactory _factory;

    public BessEmsTestServer(BatteryTestNodeManagerFactory factory)
    {
        _factory = factory;
    }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server, ApplicationConfiguration configuration)
    {
        var nodeManagers = new INodeManager[] { _factory.Create(server, configuration) };
        return new MasterNodeManager(server, configuration, null, nodeManagers);
    }
}
