namespace BatteryEms.Application.Markets;

public interface IPriceSeriesSource
{
    Task<PriceSeries> LoadAsync(PriceSeriesRequest request, CancellationToken cancellationToken);
}

public interface IPriceSeriesImportSink
{
    Task ImportAsync(PriceSeries series, CancellationToken cancellationToken);
}
