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

    // RM-M2-04 component options ------------------------------------------

    [Fact]
    public void Degradation_cost_negative_or_non_finite_rate_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DegradationCostOptions { EurPerKwhThroughput = -0.01 }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DegradationCostOptions { EurPerKwhThroughput = double.NaN }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DegradationCostOptions { EurPerKwhThroughput = double.PositiveInfinity }.EnsureValid());
    }

    [Fact]
    public void Degradation_cost_zero_rate_passes_validation()
    {
        // 0 keeps the component in the breakdown without applying a
        // penalty — explicit "configured but no rate yet".
        var validated = new DegradationCostOptions { EurPerKwhThroughput = 0 }.EnsureValid();
        Assert.Equal(0, validated.EurPerKwhThroughput);
    }

    [Fact]
    public void Soc_target_percent_outside_zero_to_hundred_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SocTargetPenaltyOptions
            {
                TargetSocPercent = -1,
                EurPerPercentDeviation = 0.1,
            }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SocTargetPenaltyOptions
            {
                TargetSocPercent = 100.1,
                EurPerPercentDeviation = 0.1,
            }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SocTargetPenaltyOptions
            {
                TargetSocPercent = double.NaN,
                EurPerPercentDeviation = 0.1,
            }.EnsureValid());
    }

    [Fact]
    public void Soc_target_negative_or_non_finite_penalty_rate_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SocTargetPenaltyOptions
            {
                TargetSocPercent = 50,
                EurPerPercentDeviation = -0.01,
            }.EnsureValid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SocTargetPenaltyOptions
            {
                TargetSocPercent = 50,
                EurPerPercentDeviation = double.NaN,
            }.EnsureValid());
    }

    [Fact]
    public void Component_options_propagate_through_solver_validation()
    {
        var options = new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = -1 },
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.EnsureValid());

        var options2 = new ScheduleSolverOptions
        {
            SocTargetPenalty = new SocTargetPenaltyOptions
            {
                TargetSocPercent = 200,
                EurPerPercentDeviation = 1,
            },
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options2.EnsureValid());
    }
}
