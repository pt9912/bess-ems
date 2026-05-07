using BatteryEms.Domain;

namespace BatteryEms.Application.Observability;

// Default sink used when no telemetry adapter is wired. Matches the
// NoOpControlCycleMetrics pattern so headless hosts (tests, dry runs)
// can drive the schedule-optimization use case without a Prometheus
// dependency or null-checks at the call site.
public sealed class NoOpOptimizationRunMetrics : IOptimizationRunMetrics
{
    public static readonly NoOpOptimizationRunMetrics Instance = new();

    public void Record(OptimizationRun run) { }
}
