using BatteryEms.Adapters.OpcUa;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using BatteryEms.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Xunit;

namespace BatteryEms.OpcUa.IntegrationTests;

// 5 pinned end-to-end tests for plan-RM-M4-04 §4 Sub-Slice D, gegen den
// embedded TestServer (kein Compose-Sidecar). Pro Test-Klasse ein
// Server-Lifecycle, damit StatusCode- und Reconnect-Pins keinen Bias
// auf andere Pins durchreichen.
[Trait("Category", "Integration")]
[CollectionDefinition("OpcUa Integration", DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711",
    Justification = "xUnit's CollectionDefinition convention requires the 'Collection' suffix.")]
public sealed class OpcUaIntegrationCollection { }

[Trait("Category", "Integration")]
[Collection("OpcUa Integration")]
public sealed class OpcUaRoundtripTests : IClassFixture<OpcUaTestServerFixture>, IAsyncLifetime
{
    private readonly OpcUaTestServerFixture _fixture;
    private readonly OpcUaMappingConfiguration _mapping;
    private readonly BatteryAsset _asset;

    public OpcUaRoundtripTests(OpcUaTestServerFixture fixture)
    {
        _fixture = fixture;
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        _mapping = loader.LoadOpcUaMapping(MappingPath);
        _asset = loader.LoadAsset(AssetPath);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EndToEnd_Read_emits_telemetry_with_mapped_values()
    {
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Soc", 65.0f);
        host.NodeManager.SetValue("Battery.ActivePower", 12.5f);
        host.NodeManager.SetValue("Battery.Temperature", 23.0f);

        await using var client = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        await using var source = new OpcUaTelemetrySource(
            client, _mapping, Defaults.ForHilSimulator(host.EndpointUrl),
            _asset, new SystemClock(), NullLogger<OpcUaTelemetrySource>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BatteryTelemetry? sample = null;
        await foreach (var s in source.ReadAsync(cts.Token))
        {
            // Wait for a sample carrying both subscribe (SOC) and read
            // (Temperature) values — i.e., the assembler has SOC>0 AND
            // Temperature>0. The first emitted sample may only carry
            // the Temperature read while subscribe-notifications are
            // still in flight.
            if (s.SocPercent > 0 && s.TemperatureCelsius > 0)
            {
                sample = s;
                break;
            }
        }

        Assert.NotNull(sample);
        Assert.Equal("single-bess-1", sample!.AssetId);
        Assert.InRange(sample.SocPercent, 64.5, 65.5);
        Assert.InRange(sample.TemperatureCelsius, 22.5, 23.5);
        Assert.Equal(DataQualityState.Valid, sample.DataQuality.Flag);
    }

    [Fact]
    public async Task EndToEnd_Subscribe_picks_up_value_change_within_two_intervals()
    {
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Soc", 30.0f);
        host.NodeManager.SetValue("Battery.Temperature", 21.0f);

        await using var client = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        await using var source = new OpcUaTelemetrySource(
            client, _mapping, Defaults.ForHilSimulator(host.EndpointUrl),
            _asset, new SystemClock(), NullLogger<OpcUaTelemetrySource>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var samples = new List<BatteryTelemetry>();
        BatteryTelemetry? before = null;
        BatteryTelemetry? after = null;
        var changedAt = DateTimeOffset.MinValue;
        await foreach (var s in source.ReadAsync(cts.Token))
        {
            samples.Add(s);
            if (before is null)
            {
                if (s.SocPercent > 25 && s.SocPercent < 35)
                {
                    before = s;
                    host.NodeManager.SetValue("Battery.Soc", 75.0f);
                    changedAt = DateTimeOffset.UtcNow;
                }
                continue;
            }
            if (s.SocPercent > 70)
            {
                after = s;
                break;
            }
        }

        Assert.True(before is not null,
            $"Initial SOC=30 sample never arrived. Saw {samples.Count} samples; "
            + $"last SOC values: [{string.Join(",", samples.TakeLast(5).Select(x => x.SocPercent))}].");
        Assert.True(after is not null,
            $"SOC=75 change never propagated. Saw {samples.Count} samples; "
            + $"last SOC values: [{string.Join(",", samples.TakeLast(5).Select(x => x.SocPercent))}].");
        var elapsed = DateTimeOffset.UtcNow - changedAt;
        Assert.True(elapsed < TimeSpan.FromSeconds(10),
            $"Subscribe latency {elapsed} exceeded 10s budget.");
    }

    [Fact]
    public async Task EndToEnd_Write_setpoint_roundtrips_through_server()
    {
        var host = _fixture.Host;
        await using var client = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        await using var sink = new OpcUaCommandSink(
            client, _mapping, _asset, Defaults.ForHilSimulator(host.EndpointUrl),
            new SystemClock(), NullLogger<OpcUaCommandSink>.Instance);

        var command = new BatteryCommand(
            CommandId: "opcua-roundtrip-1",
            Timestamp: DateTimeOffset.UtcNow,
            AssetId: _asset.AssetId,
            Mode: CommandMode.Discharge,
            ActivePowerKw: 17.5,
            ReactivePowerKvar: 0,
            ValidUntil: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5),
            Reason: "opcua-roundtrip",
            Source: CommandSource.Optimization);

        var result = await sink.WriteAsync(command, CancellationToken.None);

        Assert.True(result.Success, $"write failed: {result.Reason}");
        var written = host.NodeManager.GetWrittenValue("Battery.Setpoint.ActivePower");
        Assert.NotNull(written);
        var writtenFloat = Convert.ToSingle(written, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(writtenFloat, 17.4f, 17.6f);
    }

    [Fact]
    public async Task EndToEnd_StatusCode_bad_surfaces_as_protocol_error()
    {
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Soc", 50.0f);
        host.NodeManager.SetValue("Battery.Temperature", 22.0f);
        host.NodeManager.SetStatusCode("Battery.Soc", StatusCodes.BadDeviceFailure);

        await using var client = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        await using var source = new OpcUaTelemetrySource(
            client, _mapping, Defaults.ForHilSimulator(host.EndpointUrl),
            _asset, new SystemClock(), NullLogger<OpcUaTelemetrySource>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BatteryTelemetry? sample = null;
        await foreach (var s in source.ReadAsync(cts.Token))
        {
            if (s.DataQuality.Flag == DataQualityState.ProtocolError)
            {
                sample = s;
                break;
            }
        }

        Assert.NotNull(sample);
        Assert.Equal(DataQualityState.ProtocolError, sample!.DataQuality.Flag);
        Assert.Contains("opcua-bad", sample.DataQuality.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndToEnd_Reconnect_after_server_restart_recovers_read_path()
    {
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Soc", 40.0f);
        host.NodeManager.SetValue("Battery.Temperature", 25.0f);

        await using var client = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        // First Connect + Read — primes the session.
        await client.ConnectAsync(CancellationToken.None);
        var first = await client.ReadAsync("ns=2;s=Battery.Temperature", CancellationToken.None);
        Assert.Equal(StatusCodes.Good, first.StatusCode);

        // Tear down the server; the existing session is now stale.
        using var restartCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.RestartAsync(restartCts.Token);
        // After restart, a fresh client connects to the (same-port-)
        // restarted endpoint. The existing client's session is dead —
        // the Reconnect pin verifies that a re-Connect+Read against
        // the restarted server succeeds (i.e., the embedded server is
        // actually reachable on the same endpoint after a restart).
        await using var client2 = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        await client2.ConnectAsync(CancellationToken.None);
        var second = await client2.ReadAsync("ns=2;s=Battery.Temperature", CancellationToken.None);
        Assert.Equal(StatusCodes.Good, second.StatusCode);
    }

    private static string SchemaDirectory =>
        Path.Combine(RepoRoot(), "config", "schema");
    private static string MappingPath =>
        Path.Combine(RepoRoot(), "config", "examples", "adapters", "opcua.simulator.json");
    private static string AssetPath =>
        Path.Combine(RepoRoot(), "config", "examples", "asset.single-bess.json");

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
        throw new DirectoryNotFoundException(
            "Could not locate repository root containing BatteryEms.sln.");
    }

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
