using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class ScheduleOptimizationRequestTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Day_ahead_request_with_aligned_horizon_and_prices_passes()
    {
        var prices = new double[24];
        for (var i = 0; i < prices.Length; i++)
        {
            prices[i] = 50 + i;
        }

        var request = new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(24),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0,
            pricesPerStep: prices,
            priceUnit: "EUR/MWh");

        Assert.Equal(24, request.StepCount);
        Assert.Equal(TimeSpan.FromHours(24), request.Horizon);
        Assert.Equal("EUR/MWh", request.PriceUnit);
        Assert.Empty(request.Inputs);
    }

    [Fact]
    public void Inputs_are_validated_via_ScheduleReference_EnsureValid()
    {
        var request = new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0,
            inputs: new[] { new ScheduleReference("asset-1", ScheduleType.DayAhead, 7) });

        Assert.Equal(7, Assert.Single(request.Inputs).Version);
    }

    [Fact]
    public void Horizon_not_aligned_with_time_step_throws()
    {
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromMinutes(90),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0));
    }

    [Fact]
    public void Reversed_horizon_throws()
    {
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart + TimeSpan.FromHours(1),
            horizonEnd: HorizonStart,
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0));
    }

    [Fact]
    public void Non_positive_time_step_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.Zero,
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0));
    }

    private static readonly double[] OnePriceFifty = { 50.0 };
    private static readonly double[] OnePriceTen = { 10.0 };

    [Fact]
    public void Prices_count_must_match_step_count()
    {
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(2),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0,
            pricesPerStep: OnePriceTen,
            priceUnit: "EUR/MWh"));
    }

    [Fact]
    public void Prices_require_a_unit()
    {
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0,
            pricesPerStep: OnePriceFifty,
            priceUnit: ""));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_finite_price_throws(double price)
    {
        var prices = new[] { price };
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0,
            pricesPerStep: prices,
            priceUnit: "EUR/MWh"));
    }

    [Fact]
    public void Blank_asset_id_throws()
    {
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationRequest(
            assetId: "",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0));
    }

    [Fact]
    public void Blank_market_bid_area_throws()
    {
        Assert.Throws<ArgumentException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "",
            baseScheduleVersion: 0));
    }

    [Fact]
    public void Negative_base_schedule_version_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: -1));
    }
}
