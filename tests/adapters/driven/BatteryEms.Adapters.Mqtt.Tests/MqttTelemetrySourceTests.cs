using System.Text;
using BatteryEms.Adapters.Mqtt;
using BatteryEms.Application.IO;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

public sealed class MqttTelemetrySourceTests
{
    [Fact]
    public async Task ReadAsync_yields_battery_telemetry_decoded_from_telemetry_topic()
    {
        var client = new FakeMqttClient();
        var clock = new MqttFixtures.FixedClock();
        var source = new MqttTelemetrySource(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var consumer = ReadFirst(source, cts.Token);

        await TestHelpers.WaitUntil(() => client.SubscribedTopics.Count > 0, TimeSpan.FromSeconds(1));
        await client.DeliverAsync(
            "battery/asset-1/telemetry",
            Encoding.UTF8.GetBytes(
                """{"offset_millis":0,"soc_percent":60.5,"soh_percent":99,"active_power_kw":-25,"reactive_power_kvar":0,"dc_voltage":800,"dc_current":-31,"temperature_celsius":22,"available":true,"fault_status":"ok"}"""));

        var telemetry = await consumer;
        Assert.Equal(60.5, telemetry.SocPercent);
        Assert.Equal(-25, telemetry.ActivePowerKw);
        Assert.Equal(22, telemetry.TemperatureCelsius);
        Assert.True(telemetry.Available);
        Assert.Equal("ok", telemetry.FaultStatus);
        Assert.Equal("asset-1", telemetry.AssetId);
        Assert.Equal(MqttFixtures.Now, telemetry.Timestamp);
        Assert.Equal(DataQualityState.Valid, telemetry.DataQuality.Flag);
    }

    [Fact]
    public async Task ReadAsync_subscribes_to_resolved_telemetry_topic()
    {
        var client = new FakeMqttClient();
        var source = new MqttTelemetrySource(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults("single-bess-1"), new MqttFixtures.FixedClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var consumer = ReadFirstOrCancel(source, cts.Token);

        await TestHelpers.WaitUntil(() => client.SubscribedTopics.Count > 0, TimeSpan.FromSeconds(1));
        Assert.Contains("battery/single-bess-1/telemetry", client.SubscribedTopics);
        Assert.Equal(1, client.ConnectCallCount);

        await cts.CancelAsync();
        await consumer;
    }

    [Fact]
    public async Task Status_reflects_failure_when_payload_is_malformed()
    {
        var client = new FakeMqttClient();
        var source = new MqttTelemetrySource(client, MqttFixtures.SimulatorMapping(), MqttFixtures.Defaults(), new MqttFixtures.FixedClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var consumer = ReadFirstOrCancel(source, cts.Token);

        await TestHelpers.WaitUntil(() => client.SubscribedTopics.Count > 0, TimeSpan.FromSeconds(1));
        await client.DeliverAsync("battery/asset-1/telemetry", Encoding.UTF8.GetBytes("not json"));

        await TestHelpers.WaitUntil(() => source.Status.ConsecutiveFailures >= 1, TimeSpan.FromSeconds(1));
        Assert.NotNull(source.Status.LastError);

        await cts.CancelAsync();
        await consumer;
    }

    [Fact]
    public void Constructor_throws_when_telemetry_topic_missing_from_mapping()
    {
        var mapping = new BatteryEms.Application.Configuration.MqttMappingConfiguration("p",
            new List<BatteryEms.Application.Configuration.MqttTopicMapping>
            {
                new("command", "battery/{assetId}/command", "publish", "json", false, "none"),
            });
        Assert.Throws<InvalidOperationException>(() =>
            new MqttTelemetrySource(new FakeMqttClient(), mapping, MqttFixtures.Defaults(), new MqttFixtures.FixedClock()));
    }

    private static Task<BatteryTelemetry> ReadFirst(MqttTelemetrySource source, CancellationToken ct) => Task.Run(async () =>
    {
        await foreach (var t in source.ReadAsync(ct))
        {
            return t;
        }
        throw new InvalidOperationException("no telemetry produced");
    }, ct);

    private static Task ReadFirstOrCancel(MqttTelemetrySource source, CancellationToken ct) => Task.Run(async () =>
    {
        try
        {
            await foreach (var _ in source.ReadAsync(ct))
            {
                return;
            }
        }
        catch (OperationCanceledException) { }
    }, ct);
}
