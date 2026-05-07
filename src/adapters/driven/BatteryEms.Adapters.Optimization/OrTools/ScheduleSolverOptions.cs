namespace BatteryEms.Adapters.Optimization.OrTools;

// Configuration for the production schedule-solver adapter (RM-M2-OP-05).
// `init`-only properties — review #11 frozen-config: a singleton-scoped
// adapter must never see its options change after construction. The DI
// helper builds an instance via `with`-style copy semantics inside a
// configure callback (see ScheduleSolverOptionsBuilder).
public sealed record ScheduleSolverOptions
{
    // Wall-clock budget for one solver call. Failing past this budget
    // surfaces as OptimizationSolverStatus.TimeLimit; the produced run
    // captures whatever solution (if any) the backend returned before
    // the deadline. Null means no host-side limit; the backend default
    // applies.
    public TimeSpan? TimeLimit { get; init; }

    // Optimality gap tolerance for solvers that surface one (LP-only
    // backends like GLOP ignore this). Null means use the backend
    // default.
    public double? GapTolerance { get; init; }

    // Initial state of charge (0..100 %) the LP starts from. When null
    // the adapter falls back to (MinSoc + MaxSoc)/2 of the asset's SOC
    // band so the model has a feasible starting point even when no
    // telemetry has been plumbed in yet (M3 work).
    public double? InitialSocPercent { get; init; }

    // RM-M2-04: optional configurable objective components. When null
    // the component is omitted entirely (no LP terms, no breakdown
    // entry) — that's the M2-minimal default that matches OP-OPEN-02.
    // When set, the adapter adds the LP modelling and emits one
    // OptimizationObjectiveComponent entry per active component.
    public DegradationCostOptions? DegradationCost { get; init; }
    public SocTargetPenaltyOptions? SocTargetPenalty { get; init; }

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
        DegradationCost?.EnsureValid();
        SocTargetPenalty?.EnsureValid();
        return this;
    }
}

// Mutable companion used inside the DI configure callback so callers can
// write `opt.TimeLimit = TimeSpan.FromSeconds(5)` without losing the
// init-only frozen guarantee on the live ScheduleSolverOptions.
public sealed class ScheduleSolverOptionsBuilder
{
    public TimeSpan? TimeLimit { get; set; }
    public double? GapTolerance { get; set; }
    public double? InitialSocPercent { get; set; }
    public DegradationCostOptions? DegradationCost { get; set; }
    public SocTargetPenaltyOptions? SocTargetPenalty { get; set; }

    internal ScheduleSolverOptions Build() => new()
    {
        TimeLimit = TimeLimit,
        GapTolerance = GapTolerance,
        InitialSocPercent = InitialSocPercent,
        DegradationCost = DegradationCost,
        SocTargetPenalty = SocTargetPenalty,
    };
}
