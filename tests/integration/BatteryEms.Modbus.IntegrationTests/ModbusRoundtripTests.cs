using BatteryEms.Adapters.Modbus;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using BatteryEms.Infrastructure.Configuration;
using Xunit;

namespace BatteryEms.Modbus.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class ModbusRoundtripTests
{
    private static string SimulatorHost =>
        Environment.GetEnvironmentVariable("MODBUS_HOST") ?? "127.0.0.1";

    private static int SimulatorPort =>
        int.TryParse(Environment.GetEnvironmentVariable("MODBUS_PORT"), out var port) ? port : 5020;

    private static string SchemaDirectory =>
        Path.Combine(RepoRoot(), "config", "schema");

    private static string MappingPath =>
        Path.Combine(RepoRoot(), "tests", "integration", "fixtures", "modbus.simulator.json");

    private static string AssetPath =>
        Path.Combine(RepoRoot(), "config", "examples", "asset.single-bess.json");

    [Fact]
    public async Task TelemetrySource_reads_first_snapshot_from_running_simulator()
    {
        await WaitForTcpAsync(SimulatorHost, SimulatorPort, TimeSpan.FromSeconds(30));

        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadModbusMapping(MappingPath);

        await using var client = new FluentModbusClient(SimulatorHost, SimulatorPort);
        var source = new ModbusTelemetrySource(
            client,
            mapping,
            new ModbusAdapterOptions(
                Host: SimulatorHost,
                Port: SimulatorPort,
                AssetId: "single-bess-1",
                PollingInterval: TimeSpan.FromMilliseconds(100),
                ReadTimeout: TimeSpan.FromSeconds(2)),
            new SystemClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BatteryTelemetry? telemetry = null;
        await foreach (var sample in source.ReadAsync(cts.Token))
        {
            telemetry = sample;
            break;
        }

        Assert.NotNull(telemetry);
        Assert.Equal("single-bess-1", telemetry!.AssetId);
        Assert.Equal(60.5, telemetry.SocPercent, precision: 1);
        Assert.True(telemetry.Available);
        Assert.Equal(22, telemetry.TemperatureCelsius, precision: 1);
        Assert.Equal(DataQualityState.Valid, telemetry.DataQuality.Flag);
        Assert.True(source.Status.Connected);
        Assert.Null(source.Status.LastError);
    }

    [Fact]
    public async Task CommandSink_writes_setpoint_without_protocol_error()
    {
        await WaitForTcpAsync(SimulatorHost, SimulatorPort, TimeSpan.FromSeconds(30));

        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadModbusMapping(MappingPath);

        var asset = loader.LoadAsset(AssetPath);

        await using var client = new FluentModbusClient(SimulatorHost, SimulatorPort);
        var sink = new ModbusCommandSink(
            client,
            mapping,
            asset,
            new ModbusAdapterOptions(
                Host: SimulatorHost,
                Port: SimulatorPort,
                AssetId: "single-bess-1",
                PollingInterval: TimeSpan.FromMilliseconds(100),
                ReadTimeout: TimeSpan.FromSeconds(2)),
            new SystemClock());

        var command = new BatteryCommand(
            CommandId: "integration-1",
            Timestamp: DateTimeOffset.UtcNow,
            AssetId: "single-bess-1",
            Mode: CommandMode.Discharge,
            ActivePowerKw: 25,
            ReactivePowerKvar: 0,
            ValidUntil: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5),
            Reason: "integration-roundtrip",
            Source: CommandSource.Optimization);

        var result = await sink.WriteAsync(command, CancellationToken.None);

        Assert.True(result.Success, $"write failed: {result.Reason}");
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
            $"Simulator at {host}:{port} did not accept TCP connections within {timeout}: {lastError?.Message}");
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
