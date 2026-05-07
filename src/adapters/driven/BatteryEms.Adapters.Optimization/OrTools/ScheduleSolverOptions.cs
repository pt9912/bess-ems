namespace BatteryEms.Adapters.Optimization.OrTools;

// Configuration for the production schedule-solver adapter (RM-M2-OP-05).
// Plain record so consumers can build it inline (tests, manual host
// composition); IOptions<T> binding is one Configure call away once a
// host config section ships. Backend-neutral so a future second backend
// can reuse the same options shape.
public sealed record ScheduleSolverOptions
{
    // Wall-clock budget for one solver call. Failing past this budget
    // surfaces as OptimizationSolverStatus.TimeLimit; the produced run
    // captures whatever solution (if any) the backend returned before
    // the deadline. Null means no host-side limit; the backend default
    // applies.
    public TimeSpan? TimeLimit { get; set; }

    // Optimality gap tolerance for solvers that surface one (LP-only
    // backends like GLOP ignore this). Null means use the backend
    // default.
    public double? GapTolerance { get; set; }

    // Initial state of charge (0..100 %) the LP starts from. When null
    // the adapter falls back to (MinSoc + MaxSoc)/2 of the asset's SOC
    // band so the model has a feasible starting point even when no
    // telemetry has been plumbed in yet (M3 work).
    public double? InitialSocPercent { get; set; }

    // Market bid area written into the produced Schedule when no prior
    // Schedule exists for the asset/type pair to inherit from. Defaults
    // to "DE-LU" since that is the only bid area exercised in M2; once
    // a multi-area host configuration lands this stops being a default.
    public string DefaultMarketBidArea { get; set; } = "DE-LU";

    public ScheduleSolverOptions EnsureValid()
    {
        if (TimeLimit is { } limit && limit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TimeLimit), TimeLimit, "TimeLimit must be positive when set.");
        }
        if (GapTolerance is { } gap && (gap < 0 || !double.IsFinite(gap)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(GapTolerance), GapTolerance, "GapTolerance must be a finite, non-negative double when set.");
        }
        if (InitialSocPercent is { } soc && (soc < 0 || soc > 100 || !double.IsFinite(soc)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitialSocPercent), InitialSocPercent, "InitialSocPercent must be in [0, 100] when set.");
        }
        if (string.IsNullOrWhiteSpace(DefaultMarketBidArea))
        {
            throw new ArgumentException(
                "DefaultMarketBidArea must be a non-empty string.", nameof(DefaultMarketBidArea));
        }
        return this;
    }
}
