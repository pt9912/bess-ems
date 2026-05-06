using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace BatteryEms.Adapters.Mqtt;

// MQTTnet 4.x adapter for IMqttClient. Subscriptions are multiplexed in
// process: each topic registers once at the broker and any number of
// adapter-side handlers fan-out from ApplicationMessageReceivedAsync.
//
// SECURITY: M1 wires the simulator counterpart (plan-RM-M1-simulator.md
// §65) over anonymous plaintext TCP. TLS, credentials, and broker auth
// are M2 work; do not point this client at production brokers.
public sealed class MqttNetClient : IMqttClient
{
    private readonly MQTTnet.Client.IMqttClient _inner;
    private readonly MqttClientOptions _connectOptions;
    private readonly TimeSpan _connectTimeout;
    private readonly ConcurrentDictionary<string, List<Func<MqttMessage, Task>>> _handlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    public MqttNetClient(MqttAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var factory = new MqttFactory();
        _inner = factory.CreateMqttClient();
        _connectOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(options.BrokerHost, options.BrokerPort)
            .WithClientId(options.ClientId)
            .WithCleanSession(true)
            .Build();
        _connectTimeout = options.ConnectTimeout;

        _inner.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
    }

    public bool IsConnected => _inner.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_inner.IsConnected)
        {
            return;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inner.IsConnected)
            {
                return;
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_connectTimeout);
            await _inner.ConnectAsync(_connectOptions, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task SubscribeAsync(string topicFilter, Func<MqttMessage, Task> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(topicFilter);
        ArgumentNullException.ThrowIfNull(handler);

        var alreadySubscribed = false;
        _handlers.AddOrUpdate(
            topicFilter,
            _ => new List<Func<MqttMessage, Task>> { handler },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(handler);
                }
                alreadySubscribed = true;
                return existing;
            });

        if (alreadySubscribed)
        {
            return;
        }

        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topicFilter, MqttQualityOfServiceLevel.AtMostOnce)
            .Build();
        await _inner.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(string topic, byte[] payload, bool retained, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(payload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retained)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build();
        await _inner.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Handlers must not propagate exceptions back into MQTTnet's dispatcher loop; per-handler errors are isolated.")]
    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        if (!_handlers.TryGetValue(topic, out var handlers))
        {
            return;
        }

        Func<MqttMessage, Task>[] snapshot;
        lock (handlers)
        {
            snapshot = handlers.ToArray();
        }

        var payload = e.ApplicationMessage.PayloadSegment.ToArray();
        var message = new MqttMessage(topic, payload);
        foreach (var handler in snapshot)
        {
            try
            {
                await handler(message).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // One handler's failure must not starve the others.
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort shutdown swallows transport errors so disposal never throws; transport may already be torn down.")]
    public async ValueTask DisposeAsync()
    {
        if (_inner.IsConnected)
        {
            try
            {
                await _inner.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort shutdown; transport may already be torn down.
            }
        }
        _inner.Dispose();
        _connectGate.Dispose();
    }
}
