namespace BatteryEms.Application.Markets;

public sealed class PriceSeries
{
    public string MarketBidArea { get; }
    public string Product { get; }
    public string PriceKind { get; }
    public string Unit { get; }
    public string Source { get; }
    public DateTimeOffset HorizonStart { get; }
    public DateTimeOffset HorizonEnd { get; }
    public TimeSpan TimeStep { get; }
    public IReadOnlyList<double> Values { get; }

    public PriceSeries(
        string marketBidArea,
        string product,
        string priceKind,
        string unit,
        string source,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd,
        TimeSpan timeStep,
        IReadOnlyList<double> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketBidArea);
        ArgumentException.ThrowIfNullOrWhiteSpace(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(priceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(values);
        if (horizonStart >= horizonEnd)
        {
            throw new ArgumentException(
                $"HorizonStart must be before HorizonEnd ({horizonStart:O} -> {horizonEnd:O}).",
                nameof(horizonStart));
        }
        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeStep), "TimeStep must be positive.");
        }
        if ((horizonEnd - horizonStart).Ticks % timeStep.Ticks != 0)
        {
            throw new ArgumentException(
                $"Horizon ({horizonEnd - horizonStart}) is not an integer multiple of TimeStep ({timeStep}).",
                nameof(timeStep));
        }

        var stepCount = (int)((horizonEnd - horizonStart).Ticks / timeStep.Ticks);
        if (values.Count != stepCount)
        {
            throw new ArgumentException(
                $"Values has {values.Count} entries but the horizon spans {stepCount} steps.",
                nameof(values));
        }
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException(
                    $"Values contains non-finite value '{value}'.",
                    nameof(values));
            }
        }

        MarketBidArea = marketBidArea;
        Product = product;
        PriceKind = priceKind;
        Unit = unit;
        Source = source;
        HorizonStart = horizonStart;
        HorizonEnd = horizonEnd;
        TimeStep = timeStep;
        Values = Array.AsReadOnly(values.ToArray());
    }

    public int StepCount => Values.Count;
}
