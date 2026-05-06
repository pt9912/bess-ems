using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class NoOpIoAdapterTests
{
    [Fact]
    public async Task NoOp_telemetry_source_yields_no_telemetry_and_reports_disconnected()
    {
        var source = new NoOpBatteryTelemetrySource();
        Assert.False(source.Status.Connected);

        var count = 0;
        await foreach (var _ in source.ReadAsync(CancellationToken.None))
        {
            count++;
        }
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task NoOp_command_sink_acknowledges_every_command_with_clock_time()
    {
        var clock = new FakeClock { UtcNow = TestFixtures.Now };
        var sink = new NoOpBatteryCommandSink(clock);
        var command = BatteryCommand.SafeStop(
            "asset-1",
            TestFixtures.Now,
            TimeSpan.FromSeconds(5),
            "test",
            CommandSource.Operator);

        var result = await sink.WriteAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("noop-sink", result.Reason);
        Assert.Equal(TestFixtures.Now, result.DispatchedAt);
    }
}
