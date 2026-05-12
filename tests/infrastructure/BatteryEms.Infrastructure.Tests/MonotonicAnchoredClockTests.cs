using BatteryEms.Infrastructure.Time;
using Xunit;

namespace BatteryEms.Infrastructure.Tests;

public sealed class MonotonicAnchoredClockTests
{
    [Fact]
    public void UtcNow_advances_from_monotonic_ticks_not_wall_clock()
    {
        var source = new ManualTimestampSource();
        var clock = new MonotonicAnchoredClock(
            new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero),
            anchorTimestamp: 0,
            timestampFrequency: 1_000,
            source.GetTimestamp);

        source.Timestamp = 250;

        Assert.Equal(
            new DateTimeOffset(2026, 5, 12, 10, 0, 0, 250, TimeSpan.Zero),
            clock.UtcNow);
    }

    [Fact]
    public void Resync_accepts_small_drift_and_reanchors()
    {
        var source = new ManualTimestampSource { Timestamp = 1_000 };
        var clock = new MonotonicAnchoredClock(
            new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero),
            anchorTimestamp: 0,
            timestampFrequency: 1_000,
            source.GetTimestamp);

        var accepted = clock.TryResync(
            new DateTimeOffset(2026, 5, 12, 10, 0, 1, 20, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(50));
        source.Timestamp = 1_100;

        Assert.True(accepted);
        Assert.Equal(
            new DateTimeOffset(2026, 5, 12, 10, 0, 1, 120, TimeSpan.Zero),
            clock.UtcNow);
    }

    [Fact]
    public void Resync_rejects_large_wall_clock_backstep()
    {
        var source = new ManualTimestampSource { Timestamp = 1_000 };
        var clock = new MonotonicAnchoredClock(
            new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero),
            anchorTimestamp: 0,
            timestampFrequency: 1_000,
            source.GetTimestamp);

        var rejected = clock.TryResync(
            new DateTimeOffset(2026, 5, 12, 9, 59, 58, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(50));

        Assert.False(rejected);
        Assert.Equal(
            new DateTimeOffset(2026, 5, 12, 10, 0, 1, TimeSpan.Zero),
            clock.UtcNow);
    }

    private sealed class ManualTimestampSource
    {
        public long Timestamp { get; set; }
        public long GetTimestamp() => Timestamp;
    }
}
