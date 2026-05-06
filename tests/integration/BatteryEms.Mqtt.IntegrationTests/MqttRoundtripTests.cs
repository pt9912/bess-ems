using BatteryEms.Adapters.Mqtt;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using BatteryEms.Infrastructure.Configuration;
using Xunit;

namespace BatteryEms.Mqtt.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class MqttRoundtripTests
{
    private static string BrokerHost =>
        Environment.GetEnvironmentVariable("MQTT_BROKER_HOST") ?? "127.0.0.1";

    private static int BrokerPort =>
        int.TryParse(Environment.GetEnvironmentVariable("MQTT_BROKER_PORT"), out var port) ? port : 1883;

    private static string AssetId =>
        Environment.GetEnvironmentVariable("MQTT_ASSET_ID") ?? "single-bess-1";

    private static string SchemaDirectory =>
        Path.Combine(RepoRoot(), "config", "schema");

    private static string MappingPath =>
        Path.Combine(RepoRoot(), "config", "examples", "adapters", "mqtt.simulator.json");

    [Fact]
    public async Task TelemetrySource_receives_first_snapshot_published_by_simulator()
    {
        await WaitForTcpAsync(BrokerHost, BrokerPort, TimeSpan.FromSeconds(30));

        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadMqttMapping(MappingPath);

        var options = new MqttAdapterOptions(
            BrokerHost: BrokerHost,
            BrokerPort: BrokerPort,
            ClientId: $"integration-telemetry-{Guid.NewGuid():N}",
            AssetId: AssetId,
            ConnectTimeout: TimeSpan.FromSeconds(5),
            CommandAckTimeout: TimeSpan.FromSeconds(2));

        await using var client = new MqttNetClient(options);
        var source = new MqttTelemetrySource(client, mapping, options, new SystemClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        BatteryTelemetry? telemetry = null;
        await foreach (var sample in source.ReadAsync(cts.Token))
        {
            telemetry = sample;
            break;
        }

        Assert.NotNull(telemetry);
        Assert.Equal(AssetId, telemetry!.AssetId);
        Assert.Equal(60.5, telemetry.SocPercent, precision: 1);
        Assert.True(telemetry.Available);
        Assert.Equal(22, telemetry.TemperatureCelsius, precision: 1);
        Assert.Equal("ok", telemetry.FaultStatus);
        Assert.Equal(DataQualityState.Valid, telemetry.DataQuality.Flag);
        Assert.True(source.Status.Connected);
        Assert.Null(source.Status.LastError);
    }

    [Fact]
    public async Task CommandSink_publishes_command_and_correlates_simulator_ack()
    {
        await WaitForTcpAsync(BrokerHost, BrokerPort, TimeSpan.FromSeconds(30));

        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadMqttMapping(MappingPath);

        var options = new MqttAdapterOptions(
            BrokerHost: BrokerHost,
            BrokerPort: BrokerPort,
            ClientId: $"integration-command-{Guid.NewGuid():N}",
            AssetId: AssetId,
            ConnectTimeout: TimeSpan.FromSeconds(5),
            // Generous ACK timeout — the simulator echoes synchronously after
            // it receives the command, but compose can take a moment to wire
            // up the broker subscriptions on cold start.
            CommandAckTimeout: TimeSpan.FromSeconds(10));

        await using var client = new MqttNetClient(options);
        var sink = new MqttCommandSink(client, mapping, options, new SystemClock());

        var command = new BatteryCommand(
            CommandId: $"integration-{Guid.NewGuid():N}",
            Timestamp: DateTimeOffset.UtcNow,
            AssetId: AssetId,
            Mode: CommandMode.Discharge,
            ActivePowerKw: 25,
            ReactivePowerKvar: 0,
            ValidUntil: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30),
            Reason: "integration-roundtrip",
            Source: CommandSource.Optimization);

        var result = await sink.WriteAsync(command, CancellationToken.None);

        Assert.True(result.Success, $"command roundtrip failed: {result.Reason}");
        Assert.Equal("accepted", result.Reason);
    }

    private static async Task WaitForTcpAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await probe.ConnectAsync(host, port, probeCts.Token);
                if (probe.Connected)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
            {
                lastError = ex;
            }
            await Task.Delay(200);
        }
        throw new InvalidOperationException(
            $"MQTT broker at {host}:{port} did not accept TCP connections within {timeout}: {lastError?.Message}");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "BatteryEms.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate repository root containing BatteryEms.sln.");
    }

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
