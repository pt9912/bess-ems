using BatteryEms.Application.Markets;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class PriceSeriesStoreTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly double[] OnePriceFortyTwo = [42.0];

    [Fact]
    public void Price_series_requires_values_to_match_the_time_grid()
    {
        var ex = Assert.Throws<ArgumentException>(() => new PriceSeries(
            marketBidArea: "DE-LU",
            product: "day_ahead",
            priceKind: "energy",
            unit: "EUR/MWh",
            source: "synthetic-test",
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(2),
            timeStep: TimeSpan.FromHours(1),
            values: OnePriceFortyTwo));

        Assert.Equal("values", ex.ParamName);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Price_series_rejects_non_finite_values(double value)
    {
        var ex = Assert.Throws<ArgumentException>(() => new PriceSeries(
            marketBidArea: "DE-LU",
            product: "day_ahead",
            priceKind: "energy",
            unit: "EUR/MWh",
            source: "synthetic-test",
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            values: new[] { value }));

        Assert.Equal("values", ex.ParamName);
    }

    [Fact]
    public async Task In_memory_store_loads_series_by_source_neutral_metadata()
    {
        var store = new InMemoryPriceSeriesStore();
        var series = ValidSeries(source: "synthetic-a", values: OnePriceFortyTwo);

        await store.ImportAsync(series, CancellationToken.None);

        var loaded = await store.LoadAsync(
            new PriceSeriesRequest(
                MarketBidArea: "DE-LU",
                Product: "day_ahead",
                PriceKind: "energy",
                Source: "synthetic-a",
                HorizonStart: HorizonStart,
                HorizonEnd: HorizonStart + TimeSpan.FromHours(1),
                TimeStep: TimeSpan.FromHours(1)),
            CancellationToken.None);

        Assert.Same(series, loaded);
        Assert.Equal("EUR/MWh", loaded.Unit);
        Assert.Equal(42.0, loaded.Values.Single());
    }

    [Fact]
    public void Price_series_values_are_defensively_copied_and_read_only()
    {
        var mutablePrice = new[] { 42.0 };

        var series = ValidSeries(source: "synthetic-a", values: mutablePrice);
        mutablePrice[0] = 100.0;

        Assert.Equal(42.0, series.Values.Single());
        Assert.False(series.Values is double[]);
    }

    [Fact]
    public async Task In_memory_store_rejects_unknown_source()
    {
        var store = new InMemoryPriceSeriesStore();
        await store.ImportAsync(ValidSeries(source: "synthetic-a", values: OnePriceFortyTwo), CancellationToken.None);

        var request = new PriceSeriesRequest(
            MarketBidArea: "DE-LU",
            Product: "day_ahead",
            PriceKind: "energy",
            Source: "synthetic-b",
            HorizonStart: HorizonStart,
            HorizonEnd: HorizonStart + TimeSpan.FromHours(1),
            TimeStep: TimeSpan.FromHours(1));

        var ex = await Assert.ThrowsAsync<PriceSeriesNotFoundException>(
            () => store.LoadAsync(request, CancellationToken.None));

        Assert.Same(request, ex.Request);
    }

    private static PriceSeries ValidSeries(string source, IReadOnlyList<double> values) => new(
        marketBidArea: "DE-LU",
        product: "day_ahead",
        priceKind: "energy",
        unit: "EUR/MWh",
        source: source,
        horizonStart: HorizonStart,
        horizonEnd: HorizonStart + TimeSpan.FromHours(values.Count),
        timeStep: TimeSpan.FromHours(1),
        values: values);
}
