using System.Runtime.CompilerServices;
using BatteryEms.Application.IO;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Worker.Tests;

public sealed class TelemetryIngestionHostedServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ingestion_pushes_every_telemetry_into_the_snapshot_store()
    {
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var clock = new FakeClock { UtcNow = Now };
        var source = new ScriptedSource(new[]
        {
            CreateTelemetry(Now, socPercent: 50),
            CreateTelemetry(Now + TimeSpan.FromMilliseconds(100), socPercent: 51),
        });
        var service = new TelemetryIngestionHostedService(
            source, snapshots, clock, NullLogger<TelemetryIngestionHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await source.WaitForDrainAsync();
        await service.StopAsync(CancellationToken.None);

        var snapshot = snapshots.GetLatest("asset-1", Now + TimeSpan.FromSeconds(1));
        Assert.NotNull(snapshot);
        Assert.Equal(51, snapshot!.Telemetry.SocPercent);
    }

    [Fact]
    public async Task Adapter_failure_does_not_kill_the_loop()
    {
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var source = new ThrowingSource();
        var service = new TelemetryIngestionHostedService(
            source, snapshots, new FakeClock { UtcNow = Now },
            NullLogger<TelemetryIngestionHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        // Give the loop a chance to tick at least once and re-attempt.
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.True(source.AttemptCount >= 1);
    }

    private static BatteryTelemetry CreateTelemetry(DateTimeOffset timestamp, double socPercent) => new(
        Timestamp: timestamp,
        AssetId: "asset-1",
        SocPercent: socPercent,
        SohPercent: 100,
        ActivePowerKw: 0,
        ReactivePowerKvar: 0,
        DcVoltage: 800,
        DcCurrent: 0,
        TemperatureCelsius: 22,
        Available: true,
        FaultStatus: "ok",
        DataQuality: DataQuality.Valid);

    private sealed class ScriptedSource : IBatteryTelemetrySource
    {
        private readonly IReadOnlyList<BatteryTelemetry> _items;
        private readonly TaskCompletionSource _drained = new();

        public ScriptedSource(IReadOnlyList<BatteryTelemetry> items) => _items = items;

        public AdapterStatus Status => AdapterStatus.Disconnected;

        public Task WaitForDrainAsync() => _drained.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public async IAsyncEnumerable<BatteryTelemetry> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in _items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            _drained.TrySetResult();
            await Task.Yield();
        }
    }

    private sealed class ThrowingSource : IBatteryTelemetrySource
    {
        public int AttemptCount { get; private set; }

        public AdapterStatus Status => AdapterStatus.Disconnected;

#pragma warning disable CS1998 // intentional async-iterator that throws synchronously
        public async IAsyncEnumerable<BatteryTelemetry> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            AttemptCount++;
            throw new InvalidOperationException("simulated adapter failure");
#pragma warning disable CS0162 // unreachable yield required by the async-iterator pattern
            yield break;
#pragma warning restore CS0162
        }
#pragma warning restore CS1998
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }
}
