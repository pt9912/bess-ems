using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class OptimizationObjectiveBreakdownTests
{
    [Fact]
    public void Sum_aggregates_components_by_signed_value()
    {
        var breakdown = new OptimizationObjectiveBreakdown(new[]
        {
            new OptimizationObjectiveComponent("energy_cost", 120.50, "EUR"),
            new OptimizationObjectiveComponent("energy_revenue", -45.25, "EUR"),
            new OptimizationObjectiveComponent("ramp_penalty", 1.75, "EUR"),
        });

        Assert.Equal(77.0, breakdown.Sum, precision: 5);
        Assert.Equal(3, breakdown.Components.Count);
    }

    [Fact]
    public void Empty_breakdown_has_zero_sum_and_no_components()
    {
        Assert.Equal(0.0, OptimizationObjectiveBreakdown.Empty.Sum);
        Assert.Empty(OptimizationObjectiveBreakdown.Empty.Components);
    }

    [Theory]
    [InlineData("", 1.0, "EUR")]
    [InlineData("name", 1.0, "")]
    public void Component_rejects_blank_name_or_unit(string name, double value, string unit)
    {
        Assert.Throws<ArgumentException>(() =>
            new OptimizationObjectiveComponent(name, value, unit).EnsureValid());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Component_rejects_non_finite_value(double value)
    {
        Assert.Throws<ArgumentException>(() =>
            new OptimizationObjectiveComponent("energy_cost", value, "EUR").EnsureValid());
    }

    [Fact]
    public void Breakdown_rejects_duplicate_component_names()
    {
        var components = new[]
        {
            new OptimizationObjectiveComponent("energy_cost", 1.0, "EUR"),
            new OptimizationObjectiveComponent("energy_cost", 2.0, "EUR"),
        };
        Assert.Throws<ArgumentException>(() => new OptimizationObjectiveBreakdown(components));
    }

    [Fact]
    public void Breakdown_rejects_invalid_component()
    {
        var components = new[]
        {
            new OptimizationObjectiveComponent("energy_cost", double.NaN, "EUR"),
        };
        Assert.Throws<ArgumentException>(() => new OptimizationObjectiveBreakdown(components));
    }

    [Fact]
    public void Breakdown_throws_for_null_components_collection()
    {
        Assert.Throws<ArgumentNullException>(() => new OptimizationObjectiveBreakdown(null!));
    }
}
