namespace BatteryEms.Application.Markets;

public sealed record PriceSeriesRequest(
    string MarketBidArea,
    string Product,
    string PriceKind,
    string Source,
    DateTimeOffset HorizonStart,
    DateTimeOffset HorizonEnd,
    TimeSpan TimeStep)
{
    public PriceSeriesRequest EnsureValid()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(MarketBidArea);
        ArgumentException.ThrowIfNullOrWhiteSpace(Product);
        ArgumentException.ThrowIfNullOrWhiteSpace(PriceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        if (HorizonStart >= HorizonEnd)
        {
            throw new ArgumentException(
                $"HorizonStart must be before HorizonEnd ({HorizonStart:O} -> {HorizonEnd:O}).",
                nameof(HorizonStart));
        }
        if (TimeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TimeStep), "TimeStep must be positive.");
        }
        if ((HorizonEnd - HorizonStart).Ticks % TimeStep.Ticks != 0)
        {
            throw new ArgumentException(
                $"Horizon ({HorizonEnd - HorizonStart}) is not an integer multiple of TimeStep ({TimeStep}).",
                nameof(TimeStep));
        }
        return this;
    }
}
