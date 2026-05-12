using BatteryEms.Application.Optimization;
using BatteryEms.Domain;

namespace BatteryEms.Application.Observability;

// RM-M5-05: Sidecar-specific observability for optimization-core.
// Kept framework-free like IOptimizationRunMetrics; the Prometheus
// adapter maps these calls onto labelled instruments.
public interface IOptimizationCoreMetrics
{
    void RecordRun(
        string assetId,
        OptimizationSolverStatus status,
        string fallbackSource,
        string fallbackReason,
        OptimizationTerminalState terminalState,
        TimeSpan duration);

    void RecordSidecarHealth(string status);
}
