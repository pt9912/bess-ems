using BatteryEms.Application.Optimization;
using BatteryEms.Domain;

namespace BatteryEms.Application.Observability;

public sealed class NoOpOptimizationCoreMetrics : IOptimizationCoreMetrics
{
    public static readonly NoOpOptimizationCoreMetrics Instance = new();

    public void RecordRun(
        string assetId,
        OptimizationSolverStatus status,
        string fallbackSource,
        string fallbackReason,
        OptimizationTerminalState terminalState,
        TimeSpan duration) { }

    public void RecordSidecarHealth(string status) { }
}
