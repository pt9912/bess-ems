using BatteryEms.Adapters.Optimization.OrTools;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

public sealed class ScheduleSolverOptionsTests
{
    [Fact]
    public void Defaults_pass_validation()
    {
        var options = new ScheduleSolverOptions();
        var validated = options.EnsureValid();
        Assert.Same(options, validated);
        Assert.Equal("DE-LU", options.DefaultMarketBidArea);
        Assert.Null(options.TimeLimit);
        Assert.Null(options.GapTolerance);
        Assert.Null(options.InitialSocPercent);
    }

    [Fact]
    public void Zero_or_negative_time_limit_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { TimeLimit = TimeSpan.Zero }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { TimeLimit = TimeSpan.FromSeconds(-1) }.EnsureValid());
    }

    [Fact]
    public void Negative_or_non_finite_gap_tolerance_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { GapTolerance = -0.01 }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { GapTolerance = double.NaN }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { GapTolerance = double.PositiveInfinity }.EnsureValid());
    }

    [Fact]
    public void Initial_soc_outside_zero_to_hundred_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { InitialSocPercent = -0.1 }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { InitialSocPercent = 100.1 }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleSolverOptions { InitialSocPercent = double.NaN }.EnsureValid());
    }

    [Fact]
    public void Blank_default_market_bid_area_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ScheduleSolverOptions { DefaultMarketBidArea = "" }.EnsureValid());
        Assert.Throws<ArgumentException>(() =>
            new ScheduleSolverOptions { DefaultMarketBidArea = "   " }.EnsureValid());
    }
}
