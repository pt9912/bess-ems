using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class DefaultScheduleTrackerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 5, 6, 0, 0, 0, TimeSpan.Zero);

    private static Schedule TwoHourDayAhead(string assetId = "asset-1") =>
        new(assetId, ScheduleType.DayAhead, "DE-LU", 1, new List<ScheduleWindow>
        {
            new(Start, Start + TimeSpan.FromHours(1), 30),
            new(Start + TimeSpan.FromHours(1), Start + TimeSpan.FromHours(2), -20),
        });

    [Fact]
    public void Returns_binding_day_ahead_commitment_for_moment_inside_window()
    {
        var repo = new InMemoryScheduleRepository(new[] { TwoHourDayAhead() });
        var tracker = new DefaultScheduleTracker(repo);

        var commitments = tracker.GetActiveCommitments("asset-1", Start + TimeSpan.FromMinutes(30));

        var single = Assert.Single(commitments);
        Assert.Equal(MarketType.DayAhead, single.Market);
        Assert.Equal(30, single.PowerKw);
        Assert.Equal(CommitmentBindingState.Binding, single.BindingState);
        Assert.Equal(Start, single.WindowStart);
        Assert.Equal(Start + TimeSpan.FromHours(1), single.WindowEnd);
    }

    [Fact]
    public void Returns_empty_when_moment_is_outside_horizon()
    {
        var repo = new InMemoryScheduleRepository(new[] { TwoHourDayAhead() });
        var tracker = new DefaultScheduleTracker(repo);

        Assert.Empty(tracker.GetActiveCommitments("asset-1", Start - TimeSpan.FromSeconds(1)));
        Assert.Empty(tracker.GetActiveCommitments("asset-1", Start + TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Returns_one_commitment_per_schedule_type_with_a_covering_window()
    {
        var dayAhead = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, new List<ScheduleWindow>
        {
            new(Start, Start + TimeSpan.FromHours(1), 30),
        });
        var intraday = new Schedule("asset-1", ScheduleType.Intraday, "DE-LU", 1, new List<ScheduleWindow>
        {
            new(Start, Start + TimeSpan.FromMinutes(15), 5),
        });
        var repo = new InMemoryScheduleRepository(new[] { dayAhead, intraday });
        var tracker = new DefaultScheduleTracker(repo);

        var commitments = tracker.GetActiveCommitments("asset-1", Start + TimeSpan.FromMinutes(5));

        Assert.Equal(2, commitments.Count);
        Assert.Contains(commitments, c => c.Market == MarketType.DayAhead && c.BindingState == CommitmentBindingState.Binding);
        // Intraday and RL-Reserve windows surface as Pending so the optimiser
        // can opt in without a binding contract being implied (M1 scope).
        Assert.Contains(commitments, c => c.Market == MarketType.Intraday && c.BindingState == CommitmentBindingState.Pending);
    }

    [Fact]
    public void Skips_schedules_whose_horizon_does_not_cover_the_moment()
    {
        // Two assets in the repo, but the tracker query is asset-1 only.
        var repo = new InMemoryScheduleRepository(new[]
        {
            TwoHourDayAhead("asset-1"),
            TwoHourDayAhead("asset-2"),
        });
        var tracker = new DefaultScheduleTracker(repo);

        var commitments = tracker.GetActiveCommitments("asset-1", Start + TimeSpan.FromMinutes(30));

        var single = Assert.Single(commitments);
        Assert.Equal(MarketType.DayAhead, single.Market);
    }

    [Fact]
    public void Returns_empty_for_unknown_asset()
    {
        var tracker = new DefaultScheduleTracker(new InMemoryScheduleRepository());
        Assert.Empty(tracker.GetActiveCommitments("ghost", Start));
    }

    [Fact]
    public void Commitment_carries_market_bid_area_from_originating_schedule()
    {
        // RM-M2-02 / LH-MKT-007: market area is an attribute of the
        // commitment, not just a back-reference to the schedule. The
        // schedule's MarketBidArea must propagate verbatim.
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "AT", 1, new List<ScheduleWindow>
        {
            new(Start, Start + TimeSpan.FromHours(1), 12),
        });
        var tracker = new DefaultScheduleTracker(new InMemoryScheduleRepository(new[] { schedule }));

        var commitments = tracker.GetActiveCommitments("asset-1", Start);

        var single = Assert.Single(commitments);
        Assert.Equal("AT", single.MarketBidArea);
    }

    [Fact]
    public void Tracker_returns_consistent_commitments_across_dst_spring_forward()
    {
        // RM-M2-02 / LH-MKT-007 acceptance: tests cover at least one DST
        // boundary. Spring-forward 2026-03-29 in Europe/Berlin: 02:00 CET
        // → 03:00 CEST. In UTC (storage), the moment 01:00Z is the
        // continuous successor of 00:59Z; the schedule never sees the
        // jump and the tracker must surface a single commitment per
        // moment on either side.
        var dstStart = new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero);
        var schedule = new Schedule("asset-1", ScheduleType.DayAhead, "DE-LU", 1, new List<ScheduleWindow>
        {
            new(dstStart, dstStart + TimeSpan.FromHours(1), 10),     // 23:00Z .. 00:00Z
            new(dstStart + TimeSpan.FromHours(1), dstStart + TimeSpan.FromHours(2), 20),   // 00:00Z .. 01:00Z (CET 01..02)
            new(dstStart + TimeSpan.FromHours(2), dstStart + TimeSpan.FromHours(3), -15),  // 01:00Z .. 02:00Z (CEST 03..04)
            new(dstStart + TimeSpan.FromHours(3), dstStart + TimeSpan.FromHours(4), -25),  // 02:00Z .. 03:00Z (CEST 04..05)
        });
        var tracker = new DefaultScheduleTracker(new InMemoryScheduleRepository(new[] { schedule }));

        // Last UTC moment that maps to local CET (02:59:59 CET == 00:59:59Z+1d).
        var lastCet = new DateTimeOffset(2026, 3, 29, 0, 59, 59, TimeSpan.Zero);
        var atLastCet = Assert.Single(tracker.GetActiveCommitments("asset-1", lastCet));
        Assert.Equal(20, atLastCet.PowerKw);

        // First UTC moment after the wall-clock jump (03:00:00 CEST == 01:00:00Z+1d).
        var firstCest = new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero);
        var atFirstCest = Assert.Single(tracker.GetActiveCommitments("asset-1", firstCest));
        Assert.Equal(-15, atFirstCest.PowerKw);

        // The half-open interval boundary itself (exact 01:00:00Z, the
        // start of the post-jump window) is covered by the new window,
        // not the old one.
        var atBoundary = Assert.Single(tracker.GetActiveCommitments("asset-1",
            new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero)));
        Assert.Equal(-15, atBoundary.PowerKw);
    }
}
