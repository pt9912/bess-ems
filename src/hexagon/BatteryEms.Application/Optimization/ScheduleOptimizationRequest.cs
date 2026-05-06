using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Inputs to a horizon-level schedule optimisation (LH-OPT-001/007/008).
//
// IScheduleOptimizer differs from IDispatchOptimizer in three ways:
//   1. Time scope: a horizon (typically 24 h with 1 h time-step) rather
//      than a single 1-Hz regulation tick.
//   2. Output: a Domain.Schedule that downstream IScheduleTracker /
//      IDispatchOptimizer consume; not a one-shot setpoint.
//   3. Cost model: an explicit objective that the LP/MILP solver
//      minimises, expressed via Prices and constraint hooks rather than
//      the safety/limit clamping the dispatch path performs.
//
// Prices are optional but, when present, must align with the time grid:
// exactly one entry per time-step from HorizonStart inclusive to
// HorizonEnd exclusive. PriceUnit spells out the denominator
// (LH-OPT-008), e.g. "EUR/MWh"; the optimiser multiplies by E = P · Δt
// per step to produce the objective contribution.
public sealed class ScheduleOptimizationRequest
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

    public ScheduleOptimizationRequest(
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
