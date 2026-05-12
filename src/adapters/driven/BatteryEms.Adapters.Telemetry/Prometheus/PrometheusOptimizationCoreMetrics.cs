using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Prometheus;

namespace BatteryEms.Adapters.Telemetry.Prometheus;

// RM-M5-05: optimization-core sidecar metrics. These complement the
// generic OptimizationRun metrics with the M5 fallback taxonomy and
// idempotency terminal-state labels operators need during incidents.
public sealed class PrometheusOptimizationCoreMetrics : IOptimizationCoreMetrics
{
    private static readonly string[] RunLabels =
    {
        "asset_id",
        "status",
        "fallback_source",
        "fallback_reason",
        "terminal_state",
    };

    private static readonly string[] HealthLabels = { "status" };
    private static readonly string[] KnownHealthStatuses =
    {
        "serving",
        "not_serving",
        "unavailable",
        "contract_incompatible",
    };

    private static readonly double[] RuntimeBuckets =
        { 0.005, 0.01, 0.05, 0.1, 0.5, 1.0, 5.0, 15.0, 60.0, 300.0, 600.0 };

    private readonly Counter _runs;
    private readonly Histogram _duration;
    private readonly Counter _terminalStates;
    private readonly Gauge _sidecarHealth;

    public PrometheusOptimizationCoreMetrics()
        : this(Metrics.DefaultRegistry)
    {
    }

    public PrometheusOptimizationCoreMetrics(CollectorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var factory = Metrics.WithCustomRegistry(registry);

        _runs = factory.CreateCounter(
            "bess_optimization_core_runs_total",
            "optimization-core runs by solver status, fallback taxonomy and terminal state (RM-M5-05).",
            new CounterConfiguration { LabelNames = RunLabels });

        _duration = factory.CreateHistogram(
            "bess_optimization_core_run_duration_seconds",
            "Wall-clock duration of optimization-core calls by fallback taxonomy and terminal state.",
            new HistogramConfiguration
            {
                LabelNames = RunLabels,
                Buckets = RuntimeBuckets,
            });

        _terminalStates = factory.CreateCounter(
            "bess_optimization_core_terminal_states_total",
            "Idempotency terminal states for optimization-core requests.",
            new CounterConfiguration { LabelNames = RunLabels });

        _sidecarHealth = factory.CreateGauge(
            "bess_optimization_core_sidecar_health_status",
            "Last observed optimization-core health state. The current state is 1, known inactive states are 0.",
            new GaugeConfiguration { LabelNames = HealthLabels });
    }

    public void RecordRun(
        string assetId,
        OptimizationSolverStatus status,
        string fallbackSource,
        string fallbackReason,
        OptimizationTerminalState terminalState,
        TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackReason);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration), "Optimization-core duration must be non-negative.");
        }

        var labels = new[]
        {
            assetId,
            SolverStatusLabel(status),
            fallbackSource,
            fallbackReason,
            TerminalStateLabel(terminalState),
        };
        _runs.WithLabels(labels).Inc();
        _duration.WithLabels(labels).Observe(duration.TotalSeconds);
        _terminalStates.WithLabels(labels).Inc();
    }

    public void RecordSidecarHealth(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        foreach (var known in KnownHealthStatuses)
        {
            _sidecarHealth.WithLabels(known).Set(0);
        }
        _sidecarHealth.WithLabels(status).Set(1);
    }

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

    private static string TerminalStateLabel(OptimizationTerminalState state) => state switch
    {
        OptimizationTerminalState.Pending => "pending",
        OptimizationTerminalState.SidecarCommitted => "sidecar_committed",
        OptimizationTerminalState.FallbackCommitted => "fallback_committed",
        OptimizationTerminalState.Cancelled => "cancelled",
        OptimizationTerminalState.FailedNoActivation => "failed_no_activation",
        OptimizationTerminalState.LateResponseIgnored => "late_response_ignored",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown terminal state."),
    };
}
