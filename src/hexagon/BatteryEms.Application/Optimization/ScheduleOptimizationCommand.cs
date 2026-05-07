using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Caller-facing inputs to a schedule-optimisation run (driving-port
// shape). The API endpoint constructs this from the HTTP body; the use
// case enriches it into a full ScheduleOptimizationRequest by resolving
// the schedule identity (market bid area + base version) from the
// existing schedule under a per-(asset, type) lock.
//
// Splitting Command from Request keeps versioning policy in the use
// case — neither the API nor the driven optimizer touches
// IScheduleRepository to derive identity (RM-M2-OP-05 review #1/#3).
//
// All caller-side validation lives here (review C7) so a bad HTTP body
// surfaces as a 400 from the API endpoint instead of getting deferred
// into the use case where it would surface as a 500. Request only adds
// identity-field validation on top, and is composed from this Command —
// so adding a new caller field touches one type, not three.
public sealed class ScheduleOptimizationCommand
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

    public ScheduleOptimizationCommand(
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
        if (horizonStart >= horizonEnd)
        {
            throw new ArgumentException(
                $"HorizonStart must be before HorizonEnd ({horizonStart:O} -> {horizonEnd:O}).",
                nameof(horizonStart));
        }
        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeStep), "TimeStep must be positive (LH-OPT-008).");
        }
        if ((horizonEnd - horizonStart).Ticks % timeStep.Ticks != 0)
        {
            throw new ArgumentException(
                $"Horizon ({horizonEnd - horizonStart}) is not an integer multiple of TimeStep ({timeStep}); " +
                "the time grid must align with the horizon (LH-OPT-008).",
                nameof(timeStep));
        }

        var stepCount = (int)((horizonEnd - horizonStart).Ticks / timeStep.Ticks);
        if (pricesPerStep is not null)
        {
            if (pricesPerStep.Count != stepCount)
            {
                throw new ArgumentException(
                    $"PricesPerStep has {pricesPerStep.Count} entries but the horizon spans {stepCount} steps.",
                    nameof(pricesPerStep));
            }
            foreach (var price in pricesPerStep)
            {
                if (!double.IsFinite(price))
                {
                    throw new ArgumentException(
                        $"PricesPerStep contains non-finite value '{price}'.",
                        nameof(pricesPerStep));
                }
            }
            if (string.IsNullOrWhiteSpace(priceUnit))
            {
                throw new ArgumentException(
                    "PriceUnit is required when PricesPerStep is set (LH-OPT-008).",
                    nameof(priceUnit));
            }
        }

        if (inputs is not null)
        {
            foreach (var input in inputs)
            {
                ArgumentNullException.ThrowIfNull(input);
                input.EnsureValid();
            }
        }

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

    public TimeSpan Horizon => HorizonEnd - HorizonStart;
    public int StepCount => (int)((HorizonEnd - HorizonStart).Ticks / TimeStep.Ticks);
}
