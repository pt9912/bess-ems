using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

public sealed class FakeOpcUaClientTests
{
    [Fact]
    public async Task Initial_state_is_disconnected()
    {
        await using var client = new FakeOpcUaClient();
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Connect_then_disconnect_toggles_is_connected()
    {
        await using var client = new FakeOpcUaClient();

        await client.ConnectAsync(CancellationToken.None);
        Assert.True(client.IsConnected);

        await client.DisconnectAsync(CancellationToken.None);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Read_returns_set_value_with_status_code()
    {
        await using var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        client.SetValue("ns=2;s=Soc", 42.5, statusCode: 0x40A40000u);

        var result = await client.ReadAsync("ns=2;s=Soc", CancellationToken.None);

        Assert.Equal("ns=2;s=Soc", result.NodeId);
        Assert.Equal(42.5, result.Value);
        Assert.Equal(0x40A40000u, result.StatusCode);
    }

    [Fact]
    public async Task Read_when_not_connected_throws()
    {
        await using var client = new FakeOpcUaClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadAsync("ns=2;s=Soc", CancellationToken.None));
    }

    [Fact]
    public async Task Write_records_call_and_returns_good_status()
    {
        await using var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);

        var result = await client.WriteAsync(
            "ns=2;s=Setpoint", 25.0, OpcUaDataType.Double, CancellationToken.None);

        Assert.Equal(0x00000000u, result.StatusCode);
        Assert.Single(client.Writes);
        var write = client.Writes[0];
        Assert.Equal("ns=2;s=Setpoint", write.NodeId);
        Assert.Equal(25.0, write.Value);
        Assert.Equal(OpcUaDataType.Double, write.DataType);
    }

    [Fact]
    public async Task Write_with_preset_status_code_returns_failure_and_does_not_apply_value()
    {
        await using var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        client.SetValue("ns=2;s=Setpoint", 0.0);
        client.SetWriteStatusCode("ns=2;s=Setpoint", 0x801A0000u);

        var result = await client.WriteAsync(
            "ns=2;s=Setpoint", 25.0, OpcUaDataType.Double, CancellationToken.None);

        Assert.Equal(0x801A0000u, result.StatusCode);
        var read = await client.ReadAsync("ns=2;s=Setpoint", CancellationToken.None);
        Assert.Equal(0.0, read.Value);
    }

    [Fact]
    public async Task Subscription_round_trip_pushes_notification_to_consumer()
    {
        await using var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);

        var subscription = await client.CreateSubscriptionAsync(
            publishingIntervalMs: 1000, CancellationToken.None);
        await using (subscription.ConfigureAwait(false))
        {
            await subscription.AddMonitoredItemAsync(
                "ns=2;s=Soc", OpcUaDataType.Double, samplingIntervalMs: 250,
                CancellationToken.None);

            var fake = (FakeOpcUaSubscription)subscription;
            Assert.Single(fake.Items);
            Assert.Equal(250, fake.Items[0].SamplingIntervalMs);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var enumerator = subscription.NotificationsAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator.ConfigureAwait(false))
            {
                fake.PushNotification(new OpcUaNotification(
                    "ns=2;s=Soc", 55.0, 0u, DateTimeOffset.UtcNow));

                Assert.True(await enumerator.MoveNextAsync());
                Assert.Equal(55.0, enumerator.Current.Value);
            }
        }
    }

    [Fact]
    public async Task Create_subscription_with_non_positive_interval_throws()
    {
        await using var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.CreateSubscriptionAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task Dispose_async_is_idempotent()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);

        await client.DisposeAsync();
        await client.DisposeAsync();

        // Post-dispose: subsequent ops throw ObjectDisposed-like.
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Dispose_async_disposes_active_subscriptions()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sub = (FakeOpcUaSubscription)await client.CreateSubscriptionAsync(
            publishingIntervalMs: 1000, CancellationToken.None);

        await client.DisposeAsync();

        // The subscription's notification stream is completed.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var enumerator = sub.NotificationsAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await using (enumerator.ConfigureAwait(false))
        {
            Assert.False(await enumerator.MoveNextAsync());
        }
    }

    [Fact]
    public async Task Read_with_cancelled_token_throws()
    {
        await using var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ReadAsync("ns=2;s=x", cts.Token));
    }

    [Fact]
    public async Task Read_empty_node_id_throws()
    {
        await using var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ReadAsync("", CancellationToken.None));
    }
}
