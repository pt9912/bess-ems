using BatteryEms.Adapters.Modbus;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using BatteryEms.Infrastructure.Configuration;
using Xunit;

namespace BatteryEms.Hil.IntegrationTests;

// RM-M2-HIL-07: optional integration test against the external
// bess-hil-simulator. The test is wired into a separate compose
// stack (tests/hil/compose.yml) and only runs through
// `make test-hil-modbus`; it is NOT part of the M1 mandatory gate.
//
// Verifies the round trip the HIL profile is meant to exercise:
// the active-power setpoint travels through ModbusCommandSink (FC03
// write to a holding register, low_high float32) and the HIL device
// reflects the dynamic active-power response back through
// ModbusTelemetrySource (FC04 read from an input register, same
// word order). The simulator implements PCS dynamics, so the test
// asserts convergence within a tolerance rather than an exact match.
[Trait("Category", "HIL")]
public sealed class HilModbusRoundtripTests
{
    private static string SimulatorHost =>
        Environment.GetEnvironmentVariable("MODBUS_HOST") ?? "127.0.0.1";

    private static int SimulatorPort =>
        int.TryParse(Environment.GetEnvironmentVariable("MODBUS_PORT"), out var port) ? port : 502;

    private static string SchemaDirectory =>
        Path.Combine(RepoRoot(), "config", "schema");

    private static string MappingPath =>
        Environment.GetEnvironmentVariable("HIL_MAPPING") is { Length: > 0 } overridePath
            ? Path.Combine(RepoRoot(), overridePath)
            : Path.Combine(RepoRoot(), "config", "examples", "adapters", "modbus.hil-simulator.json");

    [Fact]
    public async Task Active_power_setpoint_round_trips_through_HIL_simulator()
    {
        await WaitForTcpAsync(SimulatorHost, SimulatorPort, TimeSpan.FromSeconds(30));

        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        var mapping = loader.LoadModbusMapping(MappingPath);

        // The HIL profile drives a 250 kW PCS; pick a setpoint well
        // inside the asset's safe window so AdapterWriteLimiter does
        // not clamp it.
        var asset = new BatteryAsset(
            assetId: "hil-asset",
            capacityKwh: 1000,
            maxChargePowerKw: 250,
            maxDischargePowerKw: 250,
            minSocPercent: 10,
            maxSocPercent: 90,
            chargeEfficiency: 0.95,
            dischargeEfficiency: 0.95,
            maxRampKwPerSecond: 100,
            minOperatingTemperatureCelsius: -20,
            maxOperatingTemperatureCelsius: 55);

        var options = new ModbusAdapterOptions(
            Host: SimulatorHost,
            Port: SimulatorPort,
            AssetId: "hil-asset",
            PollingInterval: TimeSpan.FromMilliseconds(100),
            ReadTimeout: TimeSpan.FromSeconds(2));

        const double setpointKw = 25.0;
        const double toleranceKw = 5.0;

        await using var writeClient = new FluentModbusClient(SimulatorHost, SimulatorPort);
        var sink = new ModbusCommandSink(writeClient, mapping, asset, options, new SystemClock());

        var command = new BatteryCommand(
            CommandId: "hil-1",
            Timestamp: DateTimeOffset.UtcNow,
            AssetId: "hil-asset",
            Mode: CommandMode.Discharge,
            ActivePowerKw: setpointKw,
            ReactivePowerKvar: 0,
            ValidUntil: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15),
            Reason: "hil-roundtrip",
            Source: CommandSource.Optimization);

        var writeResult = await sink.WriteAsync(command, CancellationToken.None);
        Assert.True(writeResult.Success, $"setpoint write failed: {writeResult.Reason}");

        // Poll the HIL device until the active-power read converges
        // to the setpoint (PCS dynamics need a moment) or the deadline
        // hits.
        await using var readClient = new FluentModbusClient(SimulatorHost, SimulatorPort);
        var source = new ModbusTelemetrySource(readClient, mapping, options, new SystemClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BatteryTelemetry? converged = null;
        await foreach (var sample in source.ReadAsync(cts.Token))
        {
            if (Math.Abs(sample.ActivePowerKw - setpointKw) <= toleranceKw)
            {
                converged = sample;
                break;
            }
        }

        Assert.NotNull(converged);
        Assert.InRange(converged!.ActivePowerKw, setpointKw - toleranceKw, setpointKw + toleranceKw);
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
            $"HIL simulator at {host}:{port} did not accept TCP connections within {timeout}: {lastError?.Message}");
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
        throw new InvalidOperationException("Could not locate repository root.");
    }

    // Mirror of the SystemClock used in BatteryEms.Modbus.IntegrationTests —
    // the production registration's clock is private; integration tests
    // each carry their own one-line implementation.
    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
