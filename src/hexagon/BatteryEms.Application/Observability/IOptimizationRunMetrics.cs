using BatteryEms.Domain;

namespace BatteryEms.Application.Observability;

// LH-MON-002 / RM-M2-OP-08: per-run observability for the schedule
// optimiser. Stays framework-free so the Application layer can call it
// without dragging prometheus-net inward; the driven adapter
// Adapters.Telemetry maps the calls onto Prometheus instruments.
//
// Every call corresponds to one finalised OptimizationRun (LH-OPT-009),
// even Failed/Infeasible ones — the audit-mandated runs all show up in
// the run counter so dashboards can surface a sudden swing toward
// non-Optimal.
public interface IOptimizationRunMetrics
{
    // One call per finalised run, after IOptimizationRunRepository.Append
    // returned. Increments the run counter per (asset, status), records
    // solver runtime as a histogram, captures the objective value as a
    // gauge ("last value"), and bumps the constraint-violation counter by
    // the number of violations attached to the run.
    void Record(OptimizationRun run);
}
