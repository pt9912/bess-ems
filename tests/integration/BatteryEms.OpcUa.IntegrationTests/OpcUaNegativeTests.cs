using BatteryEms.Adapters.OpcUa;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using BatteryEms.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OpcUa.IntegrationTests;

// Plan-RM-M4-08-A: Negativ-/Stress-Pins für die OPC-UA-Adapter-
// Schicht, die der M4-04-D-Happy-Path-Linie nicht abdeckt — eigene
// Datei statt Roundtrip-Tests-Erweiterung (D-06: Stilrichtungen
// trennen, Reviewability). Per-class Fixture-Instanz pro D-06; jeder
// Test resettet den NodeManager-Baseline via `_fixture.ResetNode-
// Baseline()` (M7-Pattern aus M4-04-D).
[Trait("Category", "Integration")]
[Collection("OpcUa Integration")]
public sealed class OpcUaNegativeTests : IClassFixture<OpcUaTestServerFixture>, IAsyncLifetime
{
    private readonly OpcUaTestServerFixture _fixture;
    private readonly OpcUaMappingConfiguration _mapping;
    private readonly BatteryAsset _asset;

    public OpcUaNegativeTests(OpcUaTestServerFixture fixture)
    {
        _fixture = fixture;
        var loader = new JsonFileConfigurationLoader(SchemaDirectory);
        _mapping = loader.LoadOpcUaMapping(MappingPath);
        _asset = loader.LoadAsset(AssetPath);
    }

    public Task InitializeAsync()
    {
        _fixture.ResetNodeBaseline();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Multi_cycle_reconnect_keeps_stream_alive_and_does_not_leak_subscriptions()
    {
        // Plan-RM-M4-08-A Pin: drei Restart-Cycles in einer einzigen
        // Source-Lifetime. Beweist (a) Mid-Stream-Recovery wiederholt
        // funktioniert, (b) keine client-seitige Subscription-
        // Akkumulation in `_subscriptions` (post-Sample-Assertion
        // pro Cycle: SubscriptionCount==1; post-Dispose: ==0).
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Soc", 40.0f);

        var client = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        var source = new OpcUaTelemetrySource(
            client, _mapping, Defaults.ForHilSimulator(host.EndpointUrl),
            _asset, new SystemClock(), NullLogger<OpcUaTelemetrySource>.Instance);

        var markers = new[] { 31.0f, 37.0f, 42.0f };
        var seenMarkers = new List<double>();
        var samples = new List<BatteryTelemetry>();

        try
        {
            host.NodeManager.SetValue("Battery.Temperature", 25.0f);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var cycleIndex = 0;
            var awaitingMarker = -1.0;
            var hasSeenInitial = false;

            await foreach (var s in source.ReadAsync(cts.Token))
            {
                samples.Add(s);

                if (!hasSeenInitial && s.TemperatureCelsius > 24 && s.TemperatureCelsius < 26
                    && s.DataQuality.Flag == DataQualityState.Valid)
                {
                    hasSeenInitial = true;
                    // Trigger first cycle: restart server, set new marker.
                    using var restartCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await host.RestartAsync(restartCts.Token);
                    host.NodeManager.SetValue("Battery.Temperature", markers[0]);
                    awaitingMarker = markers[0];
                    continue;
                }

                if (awaitingMarker > 0
                    && Math.Abs(s.TemperatureCelsius - awaitingMarker) < 0.5
                    && s.DataQuality.Flag == DataQualityState.Valid)
                {
                    seenMarkers.Add(s.TemperatureCelsius);
                    // Per-cycle assertion: nach dem post-restart-Sample
                    // gibt es genau eine Subscription in der Map.
                    Assert.Equal(1, client.SubscriptionCount);

                    cycleIndex++;
                    if (cycleIndex >= markers.Length) { break; }

                    await Task.Delay(200, cts.Token); // settle
                    using var restartCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await host.RestartAsync(restartCts.Token);
                    host.NodeManager.SetValue("Battery.Temperature", markers[cycleIndex]);
                    awaitingMarker = markers[cycleIndex];
                }
            }

            Assert.Equal(markers.Length, seenMarkers.Count);
            for (var i = 0; i < markers.Length; i++)
            {
                Assert.InRange(seenMarkers[i], markers[i] - 0.5, markers[i] + 0.5);
            }
        }
        finally
        {
            await source.DisposeAsync();
            // Post-Dispose-Assertion: alle Subscriptions abgeräumt.
            Assert.Equal(0, client.SubscriptionCount);
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_source_and_sink_survive_restart_under_contention()
    {
        // Plan-RM-M4-08-A Pin: Source und Sink teilen einen
        // OpcUaClient. Während der Sink 30 Setpoint-Commands über 3s
        // schreibt, killt der Restart-Task den Server bei 1.5s. Probt
        // _connectGate × _stateGate-Zusammenspiel — die SDK-Thread-
        // Safety-Garantie für reine Read+Write deckt das nicht ab.
        var host = _fixture.Host;
        host.NodeManager.SetValue("Battery.Temperature", 22.0f);

        await using var client = new OpcUaClient(Defaults.ForHilSimulator(host.EndpointUrl));
        await using var source = new OpcUaTelemetrySource(
            client, _mapping, Defaults.ForHilSimulator(host.EndpointUrl),
            _asset, new SystemClock(), NullLogger<OpcUaTelemetrySource>.Instance);
        await using var sink = new OpcUaCommandSink(
            client, _mapping, _asset, Defaults.ForHilSimulator(host.EndpointUrl),
            new SystemClock(), NullLogger<OpcUaCommandSink>.Instance);

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var sourceCts = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);

        var samples = new List<BatteryTelemetry>();
        var sinkResults = new List<CommandDispatchResult>();
        var sinkExceptions = new List<Exception>();

        var sourceTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var s in source.ReadAsync(sourceCts.Token))
                {
                    samples.Add(s);
                }
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
        }, sourceCts.Token);

        var sinkTask = Task.Run(async () =>
        {
            for (var i = 0; i < 30 && !runCts.Token.IsCancellationRequested; i++)
            {
                var cmd = new BatteryCommand(
                    CommandId: $"concurrent-{i}",
                    Timestamp: DateTimeOffset.UtcNow,
                    AssetId: _asset.AssetId,
                    Mode: CommandMode.Discharge,
                    ActivePowerKw: 5.0 + i * 0.1,
                    ReactivePowerKvar: 0,
                    ValidUntil: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5),
                    Reason: "concurrent-stress",
                    Source: CommandSource.Optimization);
                try
                {
                    var result = await sink.WriteAsync(cmd, runCts.Token);
                    sinkResults.Add(result);
                }
                catch (OperationCanceledException) { break; }
#pragma warning disable CA1031 // Sink is contracted to surface failures via Failed-Result, not throw — we capture stray throws as test failure evidence.
                catch (Exception ex) { sinkExceptions.Add(ex); }
#pragma warning restore CA1031
                try { await Task.Delay(100, runCts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }, runCts.Token);

        var restartTask = Task.Run(async () =>
        {
            await Task.Delay(1500, runCts.Token);
            using var restartCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await host.RestartAsync(restartCts.Token);
            // Setze einen Post-Restart-Marker, damit der Source ein
            // erkennbares „nach Restart"-Sample emittieren kann.
            host.NodeManager.SetValue("Battery.Temperature", 33.0f);
        }, runCts.Token);

        // Sink + Restart fertiglaufen lassen, dann Source stoppen.
        await Task.WhenAll(sinkTask, restartTask);
        // Source noch ein paar Sekunden weiterlaufen lassen, damit
        // die Mid-Stream-Recovery + post-Restart-Sample landet.
        await Task.Delay(TimeSpan.FromSeconds(4), runCts.Token);
        sourceCts.Cancel();
        await sourceTask;

        // (a) Kein Sink-Throw: jede Write completed mit einem
        //     CommandDispatchResult; Failures haben kebab-Reasons.
        Assert.Empty(sinkExceptions);
        Assert.NotEmpty(sinkResults);
        foreach (var r in sinkResults)
        {
            if (!r.Success)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Reason),
                    "Failed sink result must carry a kebab-Reason.");
            }
        }

        // (b) Mindestens ein post-Restart-Sample mit Valid-Quality —
        //     beweist Mid-Stream-Recovery aus dem Concurrent-Pfad.
        Assert.Contains(samples,
            s => s.TemperatureCelsius > 32 && s.TemperatureCelsius < 34
                && s.DataQuality.Flag == DataQualityState.Valid);

        // (c) Client am Ende verbunden.
        Assert.True(client.IsConnected, "Client should be reconnected at the end of the run.");
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
        for (var i = 0; i < 20 && dir is not null; i++)
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
