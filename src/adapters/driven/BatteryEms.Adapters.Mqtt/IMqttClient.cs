namespace BatteryEms.Adapters.Mqtt;

// Adapter-internal port that hides the concrete MQTT transport (MQTTnet,
// stub, or future replacement). Direction note: the .NET-EMS Subscribe
// targets EMS-`subscribe` topics (telemetry, status, fault, command_ack)
// and Publish targets EMS-`publish` topics (command). The mapping side
// is owned by the source/sink classes; this port is protocol shape only.
public interface IMqttClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    // Subscribe registers handler for messages whose topic matches
    // topicFilter exactly. Multiple SubscribeAsync calls for the same
    // topic add additional handlers; the wrapper is responsible for
    // dispatching to all registered handlers and for issuing the
    // broker-level subscription only once per filter.
    Task SubscribeAsync(string topicFilter, Func<MqttMessage, Task> handler, CancellationToken cancellationToken);

    Task PublishAsync(string topic, byte[] payload, bool retained, CancellationToken cancellationToken);
}
