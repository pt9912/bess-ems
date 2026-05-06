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
}
