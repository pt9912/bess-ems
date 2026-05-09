using BatteryEms.Application.Optimization;
using BatteryEms.Domain;

namespace BatteryEms.Application.Api;

// Caller-facing inputs to an intraday-reoptimisation run (RM-M4-01).
// The API endpoint constructs this from the HTTP body; the use case
// reads the existing Intraday schedule for the asset, splits past
// vs. future windows at the supplied ResidualStart, and feeds the
// future-window slice into IScheduleOptimizer for a residual-horizon
// LP.
//
// Composition: the command wraps a ScheduleOptimizationCommand with
// ScheduleType=Intraday and HorizonStart=ResidualStart. Validation
// (UTC ordering, timeStep alignment, prices) is shared with the
// day-ahead command. The use case unwraps `Inner` directly when
// building the optimiser request — no second mapping site.
//
// Design decisions (plan-RM-M4 RM-M4-01 D-01..D-04) fall on the use
// case, not on the command: the command admits any well-formed
// residual horizon; baseline-existence and window-boundary alignment
// are enforced inside the use case.
public sealed class IntradayReoptimizationCommand
{
    internal ScheduleOptimizationCommand Inner { get; }

    public string AssetId => Inner.AssetId;
    public BatteryAsset Asset => Inner.Asset;
    public DateTimeOffset ResidualStart => Inner.HorizonStart;
    public DateTimeOffset HorizonEnd => Inner.HorizonEnd;
    public TimeSpan TimeStep => Inner.TimeStep;
    public IReadOnlyList<double>? PricesPerStep => Inner.PricesPerStep;
    public string? PriceUnit => Inner.PriceUnit;
    public IReadOnlyList<ScheduleReference> Inputs => Inner.Inputs;

    public IntradayReoptimizationCommand(
        string assetId,
        BatteryAsset asset,
        DateTimeOffset residualStart,
        DateTimeOffset horizonEnd,
        TimeSpan timeStep,
        IReadOnlyList<double>? pricesPerStep = null,
        string? priceUnit = null,
        IReadOnlyList<ScheduleReference>? inputs = null)
    {
        Inner = new ScheduleOptimizationCommand(
            assetId,
            ScheduleType.Intraday,
            asset,
            horizonStart: residualStart,
            horizonEnd: horizonEnd,
            timeStep: timeStep,
            pricesPerStep: pricesPerStep,
            priceUnit: priceUnit,
            inputs: inputs);
    }

    public TimeSpan ResidualHorizon => Inner.Horizon;
    public int StepCount => Inner.StepCount;
}
