using BatteryEms.Application.Observability;
using BatteryEms.Domain;
using Prometheus;

namespace BatteryEms.Adapters.Telemetry.Prometheus;

// Maps Application-side IOptimizationRunMetrics calls onto prometheus-
// net instruments. RM-M2-OP-08 surfaces solver runtime, run counts by
// status, the latest objective value and constraint-violation totals
// (LH-MON-002 extended for the schedule-optimisation pipeline,
// LH-OPT-009 per-run audit).
public sealed class PrometheusOptimizationRunMetrics : IOptimizationRunMetrics
{
    private static readonly string[] AssetStatusLabels = { "asset_id", "status" };
    private static readonly string[] AssetIdLabels = { "asset_id" };

    // Solver runtimes range from ms (NoOp/heuristic) to minutes (MIP).
    // Buckets cover both ends with explicit log-spaced steps so dashboards
    // can render p50/p95 across solver classes without losing resolution
    // at the fast tail.
    private static readonly double[] RuntimeBuckets =
        { 0.005, 0.01, 0.05, 0.1, 0.5, 1.0, 5.0, 15.0, 60.0, 300.0, 600.0 };

    private readonly Counter _runs;
    private readonly Histogram _runtime;
    private readonly Gauge _objectiveValue;
    private readonly Counter _constraintViolations;

    public PrometheusOptimizationRunMetrics()
        : this(Metrics.DefaultRegistry)
    {
    }

    public PrometheusOptimizationRunMetrics(CollectorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var factory = Metrics.WithCustomRegistry(registry);

        _runs = factory.CreateCounter(
            "bess_optimization_runs_total",
            "Finalised schedule-optimisation runs (LH-OPT-009 / RM-M2-OP-08).",
            new CounterConfiguration { LabelNames = AssetStatusLabels });

        _runtime = factory.CreateHistogram(
            "bess_optimization_run_duration_seconds",
            "Solver wall-clock runtime per finalised run.",
            new HistogramConfiguration
            {
                LabelNames = AssetStatusLabels,
                Buckets = RuntimeBuckets,
            });

        _objectiveValue = factory.CreateGauge(
            "bess_optimization_objective_value",
            "Objective value of the most recent run per asset (cost-positive, revenue-negative per LH-OPT-009).",
            new GaugeConfiguration { LabelNames = AssetIdLabels });

        _constraintViolations = factory.CreateCounter(
            "bess_optimization_constraint_violations_total",
            "Constraint violations reported across all runs per asset.",
            new CounterConfiguration { LabelNames = AssetIdLabels });
    }

    public void Record(OptimizationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var status = SolverStatusLabel(run.Status);

        _runs.WithLabels(run.AssetId, status).Inc();
        _runtime.WithLabels(run.AssetId, status).Observe(run.SolverRuntime.TotalSeconds);
        _objectiveValue.WithLabels(run.AssetId).Set(run.ObjectiveValue);
        if (run.ConstraintViolations.Count > 0)
        {
            _constraintViolations.WithLabels(run.AssetId).Inc(run.ConstraintViolations.Count);
        }
    }

    // Snake-case wire matches the SolverStatusWire used by the persistence
    // adapter (RM-M2-OP-06) and the API JSON converter (RM-M2-OP-07).
    // Keeping all three aligned means an operator pivoting from a /metrics
    // dashboard to a stored run row to an API response sees one taxonomy.
    private static string SolverStatusLabel(OptimizationSolverStatus status) => status switch
    {
        OptimizationSolverStatus.Optimal => "optimal",
        OptimizationSolverStatus.Feasible => "feasible",
        OptimizationSolverStatus.Infeasible => "infeasible",
        OptimizationSolverStatus.Unbounded => "unbounded",
        OptimizationSolverStatus.TimeLimit => "time_limit",
        OptimizationSolverStatus.IterationLimit => "iteration_limit",
        OptimizationSolverStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown solver status."),
    };
}
