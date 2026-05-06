using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryScheduleRepositoryTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 5, 6, 0, 0, 0, TimeSpan.Zero);

    private static Schedule Sample(string assetId, ScheduleType type, int version) =>
        new(assetId, type, "DE-LU", version, new List<ScheduleWindow>
        {
            new(Start, Start + TimeSpan.FromHours(1), 0),
            new(Start + TimeSpan.FromHours(1), Start + TimeSpan.FromHours(2), -25),
        });

    [Fact]
    public void Replace_then_FindActive_returns_latest_schedule()
    {
        var repo = new InMemoryScheduleRepository();
        var v1 = Sample("asset-1", ScheduleType.DayAhead, 1);
        var v2 = Sample("asset-1", ScheduleType.DayAhead, 2);

        repo.Replace(v1);
        Assert.Same(v1, repo.FindActive("asset-1", ScheduleType.DayAhead));

        repo.Replace(v2);
        Assert.Same(v2, repo.FindActive("asset-1", ScheduleType.DayAhead));
    }

    [Fact]
    public void FindActive_returns_null_for_unknown_asset_or_type()
    {
        var repo = new InMemoryScheduleRepository();
        Assert.Null(repo.FindActive("ghost", ScheduleType.DayAhead));

        repo.Replace(Sample("asset-1", ScheduleType.DayAhead, 1));
        Assert.Null(repo.FindActive("asset-1", ScheduleType.Intraday));
    }

    [Fact]
    public void FindAll_returns_one_schedule_per_type_for_the_asset()
    {
        var repo = new InMemoryScheduleRepository(new[]
        {
            Sample("asset-1", ScheduleType.DayAhead, 1),
            Sample("asset-1", ScheduleType.Intraday, 1),
            Sample("asset-2", ScheduleType.DayAhead, 1),
        });

        var schedules = repo.FindAll("asset-1").ToList();

        Assert.Equal(2, schedules.Count);
        Assert.Contains(schedules, s => s.Type == ScheduleType.DayAhead);
        Assert.Contains(schedules, s => s.Type == ScheduleType.Intraday);
    }

    [Fact]
    public void FindAll_returns_empty_for_unknown_asset()
    {
        var repo = new InMemoryScheduleRepository();
        Assert.Empty(repo.FindAll("ghost"));
    }
}
