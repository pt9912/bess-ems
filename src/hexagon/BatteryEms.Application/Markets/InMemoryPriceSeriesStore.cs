using System.Collections.Concurrent;

namespace BatteryEms.Application.Markets;

public sealed class InMemoryPriceSeriesStore : IPriceSeriesSource, IPriceSeriesImportSink
{
    private readonly ConcurrentDictionary<PriceSeriesKey, PriceSeries> _series = new();

    public Task ImportAsync(PriceSeries series, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        cancellationToken.ThrowIfCancellationRequested();
        _series[PriceSeriesKey.From(series)] = series;
        return Task.CompletedTask;
    }

    public Task<PriceSeries> LoadAsync(
        PriceSeriesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request.EnsureValid();

        var key = PriceSeriesKey.From(request);
        if (_series.TryGetValue(key, out var series))
        {
            return Task.FromResult(series);
        }

        throw new PriceSeriesNotFoundException(request);
    }

    private sealed record PriceSeriesKey(
        string MarketBidArea,
        string Product,
        string PriceKind,
        string Source,
        DateTimeOffset HorizonStart,
        DateTimeOffset HorizonEnd,
        TimeSpan TimeStep)
    {
        public static PriceSeriesKey From(PriceSeries series) => new(
            series.MarketBidArea,
            series.Product,
            series.PriceKind,
            series.Source,
            series.HorizonStart,
            series.HorizonEnd,
            series.TimeStep);

        public static PriceSeriesKey From(PriceSeriesRequest request) => new(
            request.MarketBidArea,
            request.Product,
            request.PriceKind,
            request.Source,
            request.HorizonStart,
            request.HorizonEnd,
            request.TimeStep);
    }
}

public sealed class PriceSeriesNotFoundException : Exception
{
    public PriceSeriesNotFoundException()
        : base("Price series was not found.")
    {
    }

    public PriceSeriesNotFoundException(string message)
        : base(message)
    {
    }

    public PriceSeriesNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PriceSeriesNotFoundException(PriceSeriesRequest request)
        : base(BuildMessage(request))
    {
        Request = request;
    }

    public PriceSeriesRequest? Request { get; }

    private static string BuildMessage(PriceSeriesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"Price series '{request.MarketBidArea}/{request.Product}/{request.PriceKind}/{request.Source}' was not found.";
    }
}
