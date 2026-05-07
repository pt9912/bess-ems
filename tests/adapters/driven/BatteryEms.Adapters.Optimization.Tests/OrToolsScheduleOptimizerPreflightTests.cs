using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

public sealed class OrToolsScheduleOptimizerPreflightTests
{
    private static readonly double[] TwoStepPrices = { 10, 20 };

    [Fact]
    public async Task Missing_prices_yields_failed_with_reason()
    {
        var optimizer = Build();
        var request = NewRequest(prices: null, priceUnit: null);

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Equal("missing-prices", result.Run.TerminationReason);
        Assert.Null(result.ProducedSchedule);
        Assert.Null(result.Run.ProducedSchedule);
    }

    [Fact]
    public async Task Unsupported_price_unit_yields_failed_with_reason_carrying_unit()
    {
        var optimizer = Build();
        var request = NewRequest(prices: TwoStepPrices, priceUnit: "EUR/kWh");

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Equal("unsupported-price-unit:EUR/kWh", result.Run.TerminationReason);
    }

    [Fact]
    public async Task Initial_soc_below_min_band_yields_failed()
    {
        var optimizer = Build(new ScheduleSolverOptions { InitialSocPercent = 5 });
        var request = NewRequest(prices: TwoStepPrices, priceUnit: "EUR/MWh");

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Equal("initial-soc-out-of-bounds", result.Run.TerminationReason);
    }

    [Fact]
    public async Task Initial_soc_above_max_band_yields_failed()
    {
        var optimizer = Build(new ScheduleSolverOptions { InitialSocPercent = 95 });
        var request = NewRequest(prices: TwoStepPrices, priceUnit: "EUR/MWh");

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Equal("initial-soc-out-of-bounds", result.Run.TerminationReason);
    }

    [Fact]
    public async Task Pre_cancelled_token_throws_before_solve()
    {
        var optimizer = Build();
        var request = NewRequest(prices: TwoStepPrices, priceUnit: "EUR/MWh");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            optimizer.OptimizeAsync(request, cts.Token));
    }

    [Fact]
    public void Null_options_throws_in_constructor()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrToolsScheduleOptimizer(
                null!,
                new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
                NullLogger<OrToolsScheduleOptimizer>.Instance));
    }

    [Fact]
    public void Null_clock_throws_in_constructor()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrToolsScheduleOptimizer(
                new ScheduleSolverOptions(),
                null!,
                NullLogger<OrToolsScheduleOptimizer>.Instance));
    }

    [Fact]
    public void Null_logger_throws_in_constructor()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrToolsScheduleOptimizer(
                new ScheduleSolverOptions(),
                new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
                null!));
    }

    [Fact]
    public async Task Null_request_throws_in_optimize()
    {
        var optimizer = Build();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            optimizer.OptimizeAsync(null!, CancellationToken.None));
    }

    private static OrToolsScheduleOptimizer Build(ScheduleSolverOptions? options = null) =>
        new(
            options ?? new ScheduleSolverOptions(),
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance);

    private static ScheduleOptimizationRequest NewRequest(
        IReadOnlyList<double>? prices,
        string? priceUnit)
    {
        return new ScheduleOptimizationRequest(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: TestFixtures.HorizonStart,
            horizonEnd: TestFixtures.HorizonStart + TimeSpan.FromHours(2),
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "DE-LU",
            baseScheduleVersion: 0,
            pricesPerStep: prices,
            priceUnit: priceUnit);
    }
}
