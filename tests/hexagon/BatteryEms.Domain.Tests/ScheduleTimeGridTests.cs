using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class ScheduleTimeGridTests
{
    [Fact]
    public void DayAhead_default_is_one_hour()
    {
        Assert.Equal(TimeSpan.FromHours(1),
            ScheduleTimeGrid.DefaultTimeStep(ScheduleType.DayAhead));
    }

    [Fact]
    public void Intraday_default_is_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15),
            ScheduleTimeGrid.DefaultTimeStep(ScheduleType.Intraday));
    }

    [Fact]
    public void RegelLeistungReserve_default_is_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15),
            ScheduleTimeGrid.DefaultTimeStep(ScheduleType.RegelLeistungReserve));
    }

    [Fact]
    public void Unknown_schedule_type_throws()
    {
        // Defensive guard for forward-compatibility: a future enum value
        // would otherwise default to TimeSpan.Zero (would break callers
        // expecting a positive step) — surface it loudly instead.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScheduleTimeGrid.DefaultTimeStep((ScheduleType)999));
    }
}
