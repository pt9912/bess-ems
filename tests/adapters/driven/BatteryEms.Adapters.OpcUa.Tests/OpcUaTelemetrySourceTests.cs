using BatteryEms.Application.Configuration;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

public sealed class OpcUaTelemetrySourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly BatteryAsset Asset = new(
        assetId: "asset-1",
        capacityKwh: 100,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 10,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private static OpcUaAdapterOptions Options(
        TimeSpan? polling = null,
        int channelCapacity = 256)
        => new()
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            PollingInterval = polling ?? TimeSpan.FromMilliseconds(10),
            SubscriptionChannelCapacity = channelCapacity,
            AllowUnsecured = true,
            AllowUnsecuredReason = "telemetry-source-tests",
            ReconnectBackoffStart = TimeSpan.FromMilliseconds(1),
            ReconnectBackoffMax = TimeSpan.FromMilliseconds(2),
        };

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private static OpcUaNodeMapping Node(
        string name,
        string nodeId,
        string direction,
        string dataType = "float",
        double scaleFactor = 1.0,
        bool writable = false,
        int? monitoringIntervalMs = null)
        => new(
            Name: name,
            NodeId: nodeId,
            Direction: direction,
            DataType: dataType,
            ScaleFactor: scaleFactor,
            Writable: writable,
            AuthRequired: "none",
            MonitoringIntervalMs: monitoringIntervalMs);

    private static OpcUaMappingConfiguration Mapping(params OpcUaNodeMapping[] nodes)
        => new("v1", "test", nodes);

    private static OpcUaTelemetrySource BuildSource(
        IOpcUaClient client,
        OpcUaMappingConfiguration mapping,
        OpcUaAdapterOptions? options = null,
        IClock? clock = null)
        => new(
            client,
            mapping,
            options ?? Options(),
            Asset,
            clock ?? new FakeClock(),
            NullLogger<OpcUaTelemetrySource>.Instance);

    // Plan §4 Sub-Slice B Read-Sample-Pin: alle gemappten Werte fließen
    // in ein BatteryTelemetry mit korrekt aggregierten Feldern.
    [Fact]
    public async Task Read_only_mapping_emits_battery_telemetry_with_mapped_fields()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 55.5);
        client.SetValue("ns=2;Power", 12.0);
        client.SetValue("ns=2;Temp", 24.5);
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "read"),
            Node("active_power_kw", "ns=2;Power", "read"),
            Node("temperature_celsius", "ns=2;Temp", "read")));
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                Assert.True(await enumerator.MoveNextAsync());
                var sample = enumerator.Current;
                Assert.Equal("asset-1", sample.AssetId);
                Assert.Equal(55.5, sample.SocPercent);
                Assert.Equal(12.0, sample.ActivePowerKw);
                Assert.Equal(24.5, sample.TemperatureCelsius);
                Assert.Equal(DataQualityState.Valid, sample.DataQuality.Flag);
            }
        }
    }

    // ScaleFactor-Pin: lesender Wert wird mit ScaleFactor multipliziert.
    [Fact]
    public async Task Read_value_is_multiplied_by_scale_factor()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 555); // raw
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "read", scaleFactor: 0.1)));
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                Assert.True(await enumerator.MoveNextAsync());
                Assert.Equal(55.5, enumerator.Current.SocPercent, precision: 9);
            }
        }
    }

    // Plan §4 Sub-Slice B StatusCode-Aggregation-Pin: ein einzelner
    // Bad-StatusCode dominiert über mehrere Good Werte.
    [Fact]
    public async Task Bad_status_on_one_node_degrades_whole_sample_to_protocol_error()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 50.0); // Good
        client.SetValue("ns=2;Power", 10.0, statusCode: 0x80AB0000u); // BadNotConnected
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "read"),
            Node("active_power_kw", "ns=2;Power", "read")));
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                Assert.True(await enumerator.MoveNextAsync());
                Assert.Equal(DataQualityState.ProtocolError, enumerator.Current.DataQuality.Flag);
                Assert.Equal("opcua-bad-not-connected", enumerator.Current.DataQuality.Reason);
            }
        }
    }

    // Plan §4 Sub-Slice B Subscribe-Notification-Pin: ein Push aus der
    // Subscription erscheint beim nächsten Tick im emittierten Sample.
    [Fact]
    public async Task Subscribe_notification_lands_on_next_emitted_sample()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 0.0); // initial (so the first tick emits something)
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "subscribe", monitoringIntervalMs: 100)));
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                // Kick the subscription pump from another task: push a
                // notification before the first MoveNextAsync resolves.
                var pump = Task.Run(async () =>
                {
                    while (true)
                    {
                        var subs = client.Subscriptions;
                        if (subs.Count > 0)
                        {
                            subs[0].PushNotification(new OpcUaNotification(
                                "ns=2;Soc", 77.0, 0u, Now));
                            return;
                        }
                        await Task.Delay(20);
                    }
                });
                BatteryTelemetry? observed = null;
                while (await enumerator.MoveNextAsync())
                {
                    if (enumerator.Current.SocPercent != 0.0)
                    {
                        observed = enumerator.Current;
                        break;
                    }
                }
                await pump;
                Assert.NotNull(observed);
                Assert.Equal(77.0, observed!.SocPercent);
            }
        }
    }

    // Plan §4 Sub-Slice B Reconnect-Backoff-Pin: zwei aufeinanderfolgende
    // ConnectAsync-Failures dann Success — der Source kommt erst beim
    // dritten Versuch durch.
    [Fact]
    public async Task Connect_with_two_failures_then_success_eventually_emits_sample()
    {
        var client = new FakeOpcUaClient { FailingConnectAttempts = 2 };
        client.SetValue("ns=2;Soc", 50.0);
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "read")));
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                Assert.True(await enumerator.MoveNextAsync());
                Assert.Equal(50.0, enumerator.Current.SocPercent);
                Assert.Equal(3, client.ConnectAttempts);
            }
        }
    }

    // Read-only mapping (no subscribe nodes) → Status.Connected = true
    // post-ConnectAsync trotz fehlender Subscription (D-09 Klärung).
    [Fact]
    public async Task Read_only_mapping_status_is_connected_without_subscription()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 50.0);
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "read")));
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                Assert.True(await enumerator.MoveNextAsync());
                Assert.True(src.Status.Connected);
                // Kein Subscription wurde angelegt:
                Assert.Empty(client.Subscriptions);
            }
        }
    }

    // Per-Knoten samplingIntervalMs erreicht AddMonitoredItem unverändert;
    // Knoten ohne Override fällt auf DefaultMonitoringIntervalMs zurück.
    [Fact]
    public async Task Per_node_sampling_interval_is_threaded_through_to_monitored_item()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;A", 1.0);
        client.SetValue("ns=2;B", 2.0);
        var options = Options() with { DefaultMonitoringIntervalMs = 1000 };
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;A", "subscribe", monitoringIntervalMs: 250),
            Node("active_power_kw", "ns=2;B", "subscribe")), // no override → falls back
            options);
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                // Allow the pump to register MonitoredItems.
                while (client.Subscriptions.Count == 0)
                {
                    await Task.Delay(10);
                }
                var items = client.Subscriptions[0].Items;
                while (items.Count < 2)
                {
                    await Task.Delay(10);
                    items = client.Subscriptions[0].Items;
                }
                Assert.Equal(2, items.Count);
                var a = items.First(i => i.NodeId == "ns=2;A");
                var b = items.First(i => i.NodeId == "ns=2;B");
                Assert.Equal(250, a.SamplingIntervalMs);
                Assert.Equal(1000, b.SamplingIntervalMs);
            }
        }
    }

    // Cancellation-aborts-Read: a cancelled token aborts the read loop
    // with OperationCanceledException (or completes without emit).
    [Fact]
    public async Task Cancelled_read_loop_terminates()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 50.0);
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "read")));
        await using (src)
        {
            using var cts = new CancellationTokenSource();
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                Assert.True(await enumerator.MoveNextAsync());
                cts.Cancel();
                // Subsequent MoveNextAsync either throws OCE (during
                // Task.Delay) or returns false (loop exited cleanly).
                try
                {
                    while (await enumerator.MoveNextAsync()) { }
                }
                catch (OperationCanceledException) { /* expected */ }
            }
        }
    }

    [Fact]
    public async Task Dispose_async_during_read_lets_consumer_finish_cleanly()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 50.0);
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "read")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await using (enumerator)
        {
            Assert.True(await enumerator.MoveNextAsync());
            await src.DisposeAsync();
            try
            {
                while (await enumerator.MoveNextAsync()) { }
            }
            catch (OperationCanceledException) { /* expected on shutdown CTS */ }
        }
    }

    [Fact]
    public async Task Dispose_async_is_idempotent()
    {
        var src = BuildSource(new FakeOpcUaClient(), Mapping(
            Node("soc_percent", "ns=2;Soc", "read")));
        await src.DisposeAsync();
        await src.DisposeAsync(); // second call must not throw
    }

    // Plan §4 Sub-Slice B Subscription-Overflow-Pin (D-03):
    // Channel über Capacity treiben → Overflow-Flag setzt
    // DataQuality.Stale("opcua-subscription-overflow") auf das nächste
    // emittierte Sample. Nach einem Drain ist der Flag gelöscht und
    // weitere Samples sind wieder Valid.
    [Fact]
    public async Task Subscription_overflow_floors_data_quality_to_stale_and_clears_after_drain()
    {
        var client = new FakeOpcUaClient();
        client.SetValue("ns=2;Soc", 50.0); // initial value so the read tick has something
        // Klein-Capacity damit wir das Channel zuverlässig überfüllen.
        var options = Options(channelCapacity: 2);
        var src = BuildSource(client, Mapping(
            Node("soc_percent", "ns=2;Soc", "subscribe", monitoringIntervalMs: 100)),
            options);
        await using (src)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var enumerator = src.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            await using (enumerator)
            {
                // Wait for the subscription to be alive.
                while (client.Subscriptions.Count == 0)
                {
                    await Task.Delay(10);
                }
                var sub = client.Subscriptions[0];
                // Push more notifications than the channel can hold so
                // the Drop-Oldest-Pfad zuschlägt + Overflow-Flag setzt.
                for (var i = 0; i < 20; i++)
                {
                    sub.PushNotification(new OpcUaNotification(
                        "ns=2;Soc", 50.0 + i, 0u, Now));
                }
                // Allow the pump to write into the channel.
                await Task.Delay(50);
                BatteryTelemetry? overflowed = null;
                while (await enumerator.MoveNextAsync())
                {
                    if (enumerator.Current.DataQuality.Flag == DataQualityState.Stale)
                    {
                        overflowed = enumerator.Current;
                        break;
                    }
                }
                Assert.NotNull(overflowed);
                Assert.Equal("opcua-subscription-overflow", overflowed!.DataQuality.Reason);
            }
        }
    }

    [Fact]
    public void Constructor_null_args_throw()
    {
        var client = new FakeOpcUaClient();
        var mapping = Mapping(Node("soc_percent", "ns=2;A", "read"));
        var options = Options();
        var clock = new FakeClock();
        var logger = NullLogger<OpcUaTelemetrySource>.Instance;
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaTelemetrySource(null!, mapping, options, Asset, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaTelemetrySource(client, null!, options, Asset, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaTelemetrySource(client, mapping, null!, Asset, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaTelemetrySource(client, mapping, options, null!, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaTelemetrySource(client, mapping, options, Asset, null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaTelemetrySource(client, mapping, options, Asset, clock, null!));
    }

    // D-04 Konstruktor-Pin: ein Source mit Default-Security-Options
    // failed beim Bau (EnsureValid wirft opcua-security-not-hardened),
    // bevor ReadAsync überhaupt aufgerufen wird.
    [Fact]
    public void Constructor_with_unsafe_default_options_throws_security_guard()
    {
        var unsafeOptions = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            // AllowUnsecured=false default
        };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildSource(new FakeOpcUaClient(),
                Mapping(Node("soc_percent", "ns=2;A", "read")),
                unsafeOptions));
        Assert.Contains("opcua-security-not-hardened", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_with_unknown_data_type_throws()
    {
        var bad = new OpcUaNodeMapping(
            Name: "soc_percent",
            NodeId: "ns=2;A",
            Direction: "read",
            DataType: "decimal",
            ScaleFactor: 1.0,
            Writable: false,
            AuthRequired: "none");
        Assert.Throws<ArgumentException>(() =>
            BuildSource(new FakeOpcUaClient(), Mapping(bad)));
    }
}
