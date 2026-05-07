using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Driven-port shape: the optimiser sees a Command (everything the
// caller knew) plus the resolved schedule identity (MarketBidArea +
// BaseScheduleVersion) the use case derived under its per-(asset,
// type) lock.
//
// Composed via ScheduleOptimizationCommand (review C7) — adding a new
// caller-facing field touches Command only, not Request, and not the
// use case's mapping site. Forwarder properties on Request preserve
// the existing optimiser access surface (`request.AssetId`,
// `request.HorizonStart`, …) so the adapter code reads naturally
// without reaching through `request.Command.X`.
//
// IScheduleOptimizer differs from IDispatchOptimizer in three ways:
//   1. Time scope: a horizon (typically 24 h with 1 h time-step) rather
//      than a single 1-Hz regulation tick.
//   2. Output: a Domain.Schedule that downstream IScheduleTracker /
//      IDispatchOptimizer consume; not a one-shot setpoint.
//   3. Cost model: an explicit objective that the LP/MILP solver
//      minimises, expressed via Prices and constraint hooks rather than
//      the safety/limit clamping the dispatch path performs.
public sealed class ScheduleOptimizationRequest
{
    public ScheduleOptimizationCommand Command { get; }

    // RM-M2-OP-05 review #1/#3: identity is resolved by the use case
    // before the optimiser is invoked. MarketBidArea is inherited from
    // the latest existing Schedule for (AssetId, ScheduleType); when no
    // prior schedule exists the use case supplies its configured default.
    // BaseScheduleVersion is the version of that prior schedule (0 when
    // none exists); the optimiser produces version BaseScheduleVersion+1.
    public string MarketBidArea { get; }
    public int BaseScheduleVersion { get; }

    public ScheduleOptimizationRequest(
        ScheduleOptimizationCommand command,
        string marketBidArea,
        int baseScheduleVersion)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketBidArea);
        if (baseScheduleVersion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseScheduleVersion), baseScheduleVersion,
                "BaseScheduleVersion must be non-negative; 0 means no prior schedule exists.");
        }

        Command = command;
        MarketBidArea = marketBidArea;
        BaseScheduleVersion = baseScheduleVersion;
    }

    // Forwarders for the existing optimiser access surface — adding a
    // new caller field would just add another forwarder line here, the
    // ctor stays untouched.
    public string AssetId => Command.AssetId;
    public ScheduleType ScheduleType => Command.ScheduleType;
    public BatteryAsset Asset => Command.Asset;
    public DateTimeOffset HorizonStart => Command.HorizonStart;
    public DateTimeOffset HorizonEnd => Command.HorizonEnd;
    public TimeSpan TimeStep => Command.TimeStep;
    public IReadOnlyList<double>? PricesPerStep => Command.PricesPerStep;
    public string? PriceUnit => Command.PriceUnit;
    public IReadOnlyList<ScheduleReference> Inputs => Command.Inputs;
    public TimeSpan Horizon => Command.Horizon;
    public int StepCount => Command.StepCount;
}
