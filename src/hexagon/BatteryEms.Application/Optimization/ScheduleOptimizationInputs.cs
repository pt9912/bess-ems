using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Caller-facing inputs to a schedule-optimisation run (driving-port
// shape). The API endpoint constructs this from the HTTP body; the use
// case enriches it into a full ScheduleOptimizationRequest by resolving
// the schedule identity (market bid area + base version) from the
// existing schedule under a per-(asset, type) lock.
//
// Splitting Inputs from Request keeps versioning policy in the use
// case — neither the API nor the driven optimizer touches
// IScheduleRepository to derive identity (RM-M2-OP-05 review #1/#3).
public sealed class ScheduleOptimizationInputs
{
    public string AssetId { get; }
    public ScheduleType ScheduleType { get; }
    public BatteryAsset Asset { get; }
    public DateTimeOffset HorizonStart { get; }
    public DateTimeOffset HorizonEnd { get; }
    public TimeSpan TimeStep { get; }
    public IReadOnlyList<double>? PricesPerStep { get; }
    public string? PriceUnit { get; }
    public IReadOnlyList<ScheduleReference> Inputs { get; }

    public ScheduleOptimizationInputs(
        string assetId,
        ScheduleType scheduleType,
        BatteryAsset asset,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd,
        TimeSpan timeStep,
        IReadOnlyList<double>? pricesPerStep = null,
        string? priceUnit = null,
        IReadOnlyList<ScheduleReference>? inputs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(asset);

        AssetId = assetId;
        ScheduleType = scheduleType;
        Asset = asset;
        HorizonStart = horizonStart;
        HorizonEnd = horizonEnd;
        TimeStep = timeStep;
        PricesPerStep = pricesPerStep;
        PriceUnit = priceUnit;
        Inputs = inputs ?? Array.Empty<ScheduleReference>();
    }
}
