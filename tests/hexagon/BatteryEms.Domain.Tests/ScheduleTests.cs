using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class ScheduleTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 5, 6, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<ScheduleWindow> ThreeHourWindows() =>
    [
        new(Start, Start + TimeSpan.FromHours(1), 0),
        new(Start + TimeSpan.FromHours(1), Start + TimeSpan.FromHours(2), -25),
        new(Start + TimeSpan.FromHours(2), Start + TimeSpan.FromHours(3), 30),
    ];

    [Fact]
    public void Window_covers_is_half_open()
    {
        var window = new ScheduleWindow(Start, Start + TimeSpan.FromHours(1), 0);

        Assert.True(window.Covers(Start));
        Assert.True(window.Covers(Start + TimeSpan.FromMinutes(30)));
        Assert.False(window.Covers(Start + TimeSpan.FromHours(1)));
        Assert.False(window.Covers(Start - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void WindowCovering_returns_window_for_moment_inside_horizon()
    {
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, ThreeHourWindows());

        var window = schedule.WindowCovering(Start + TimeSpan.FromMinutes(90));

        Assert.NotNull(window);
        Assert.Equal(-25, window!.TargetPowerKw);
    }

    [Fact]
    public void WindowCovering_returns_null_outside_horizon()
    {
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, ThreeHourWindows());

        Assert.Null(schedule.WindowCovering(Start - TimeSpan.FromSeconds(1)));
        Assert.Null(schedule.WindowCovering(Start + TimeSpan.FromHours(3)));
    }

    [Fact]
    public void WindowCovering_at_window_boundary_resolves_to_next_window()
    {
        // Half-open semantics: the moment that is exactly the end of one
        // window is NOT covered by it; it falls into the next window.
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, ThreeHourWindows());

        var atBoundary = schedule.WindowCovering(Start + TimeSpan.FromHours(1));

        Assert.NotNull(atBoundary);
        Assert.Equal(-25, atBoundary!.TargetPowerKw);
    }

    [Fact]
    public void Constructor_rejects_empty_window_list()
    {
        Assert.Throws<ArgumentException>(() =>
            new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, new List<ScheduleWindow>()));
    }

    [Fact]
    public void Constructor_rejects_window_with_start_at_or_after_end()
    {
        var bad = new List<ScheduleWindow> { new(Start, Start, 0) };
        Assert.Throws<ArgumentException>(() =>
            new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, bad));
    }

    [Fact]
    public void Constructor_rejects_overlapping_windows()
    {
        var overlap = new List<ScheduleWindow>
        {
            new(Start, Start + TimeSpan.FromHours(1), 0),
            new(Start + TimeSpan.FromMinutes(30), Start + TimeSpan.FromHours(2), -25),
        };
        Assert.Throws<ArgumentException>(() =>
            new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, overlap));
    }

    [Fact]
    public void Constructor_rejects_negative_version()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", -1, ThreeHourWindows()));
    }

    [Fact]
    public void Constructor_rejects_blank_asset_id_and_market_bid_area()
    {
        Assert.Throws<ArgumentException>(() =>
            new Schedule("", ScheduleType.DayAhead, "DE-LU", 1, ThreeHourWindows()));
        Assert.Throws<ArgumentException>(() =>
            new Schedule("asset-1", ScheduleType.DayAhead, "", 1, ThreeHourWindows()));
    }

    [Fact]
    public void Adjacent_windows_are_allowed_when_end_equals_next_start()
    {
        // Half-open intervals make this the natural shape: window N ends at
        // moment T, window N+1 starts at moment T, no overlap, no gap.
        var adjacent = new List<ScheduleWindow>
        {
            new(Start, Start + TimeSpan.FromHours(1), 0),
            new(Start + TimeSpan.FromHours(1), Start + TimeSpan.FromHours(2), 25),
        };
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, adjacent);

        Assert.Equal(2, schedule.Windows.Count);
        Assert.Equal(Start, schedule.HorizonStart);
        Assert.Equal(Start + TimeSpan.FromHours(2), schedule.HorizonEnd);
    }

    [Fact]
    public void Schedule_resolves_continuously_across_dst_spring_forward()
    {
        // Europe/Berlin spring 2026: 02:00 CET → 03:00 CEST on 2026-03-29.
        // In UTC, the storage is linear regardless: 01:00Z is the moment that
        // would have been 02:00 CET pre-jump and is 03:00 CEST post-jump.
        // The schedule never sees the jump because windows live in UTC.
        var dstStart = new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero);
        var windows = new List<ScheduleWindow>
        {
            new(dstStart, dstStart + TimeSpan.FromHours(1), 10),     // 23:00Z .. 00:00Z (00:00..01:00 CET local)
            new(dstStart + TimeSpan.FromHours(1), dstStart + TimeSpan.FromHours(2), 20),   // 00:00Z .. 01:00Z (01:00..02:00 CET)
            new(dstStart + TimeSpan.FromHours(2), dstStart + TimeSpan.FromHours(3), -15),  // 01:00Z .. 02:00Z (03:00..04:00 CEST)
            new(dstStart + TimeSpan.FromHours(3), dstStart + TimeSpan.FromHours(4), -25),  // 02:00Z .. 03:00Z (04:00..05:00 CEST)
        };
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, windows);

        // Last UTC moment that maps to local CET (02:59:59 CET) — covered by window 1.
        var lastCet = new DateTimeOffset(2026, 3, 29, 0, 59, 59, TimeSpan.Zero);
        Assert.Equal(20, schedule.WindowCovering(lastCet)!.TargetPowerKw);

        // First UTC moment after the jump (03:00:00 CEST) — covered by window 2.
        var firstCest = new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(-15, schedule.WindowCovering(firstCest)!.TargetPowerKw);

        // Continuity across the transition: HorizonEnd == windows[^1].End,
        // and there are no gaps in UTC.
        for (var i = 1; i < schedule.Windows.Count; i++)
        {
            Assert.Equal(schedule.Windows[i - 1].End, schedule.Windows[i].Start);
        }
    }
}
