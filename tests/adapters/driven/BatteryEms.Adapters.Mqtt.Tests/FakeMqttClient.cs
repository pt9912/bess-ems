using System.Collections.Concurrent;
using BatteryEms.Adapters.Mqtt;

namespace BatteryEms.Adapters.Mqtt.Tests;

internal sealed class FakeMqttClient : IMqttClient
{
    private readonly ConcurrentDictionary<string, List<Func<MqttMessage, Task>>> _handlers = new(StringComparer.Ordinal);

    public bool IsConnected { get; private set; }

    public List<(string Topic, byte[] Payload, MqttQualityOfService Qos, bool Retained)> Publishes { get; } = new();

    public List<(string Topic, MqttQualityOfService Qos)> SubscribedTopics { get; } = new();

    // Convenience projection so existing tests that only care about
    // the topic string can keep their shape. New per-channel-QoS
    // tests assert against SubscribedTopics directly.
    public IEnumerable<string> SubscribedTopicNames => SubscribedTopics.Select(t => t.Topic);

    public int ConnectCallCount { get; private set; }

    public Func<Task>? OnConnect { get; set; }

    public Func<Task>? OnPublish { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectCallCount++;
        if (OnConnect is not null)
        {
            return Connect();
        }
        IsConnected = true;
        return Task.CompletedTask;

        async Task Connect()
        {
            await OnConnect!.Invoke().ConfigureAwait(false);
            IsConnected = true;
        }
    }

    public Task SubscribeAsync(
        string topicFilter,
        MqttQualityOfService qos,
        Func<MqttMessage, Task> handler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubscribedTopics.Add((topicFilter, qos));
        _handlers.AddOrUpdate(
            topicFilter,
            _ => new List<Func<MqttMessage, Task>> { handler },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(handler);
                }
                return existing;
            });
        return Task.CompletedTask;
    }

    public async Task PublishAsync(
        string topic,
        byte[] payload,
        MqttQualityOfService qos,
        bool retained,
        CancellationToken cancellationToken)
    {
        if (OnPublish is not null)
        {
            await OnPublish.Invoke().ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Publishes.Add((topic, payload, qos, retained));
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public async Task DeliverAsync(string topic, byte[] payload)
    {
        if (!_handlers.TryGetValue(topic, out var handlers))
        {
            throw new InvalidOperationException($"no subscription registered for topic '{topic}'");
        }
        Func<MqttMessage, Task>[] snapshot;
        lock (handlers)
        {
            snapshot = handlers.ToArray();
        }
        var message = new MqttMessage(topic, payload);
        foreach (var handler in snapshot)
        {
            await handler(message).ConfigureAwait(false);
        }
    }
}
