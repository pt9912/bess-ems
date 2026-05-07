using System.Globalization;
using System.IO;
using System.Text;
using BatteryEms.Adapters.Telemetry.Prometheus;
using BatteryEms.Domain;
using Prometheus;
using Xunit;

namespace BatteryEms.Adapters.Telemetry.Tests;

public sealed class PrometheusOptimizationRunMetricsTests
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] FailedRunViolations = { "soc_floor_violated", "ramp_rate_exceeded" };

    [Fact]
    public async Task Optimal_run_increments_counter_records_runtime_and_sets_objective_gauge()
    {
        var (metrics, registry) = Build();
        metrics.Record(BuildRun(
            assetId: "asset-1",
            status: OptimizationSolverStatus.Optimal,
            solverRuntime: TimeSpan.FromSeconds(0.5),
            objectiveValue: -1234.5));

        var scrape = await ScrapeAsync(registry);
        Assert.Contains(
            "bess_optimization_runs_total{asset_id=\"asset-1\",status=\"optimal\"} 1",
            scrape, StringComparison.Ordinal);
        Assert.Contains(
            "bess_optimization_run_duration_seconds_count{asset_id=\"asset-1\",status=\"optimal\"} 1",
            scrape, StringComparison.Ordinal);
        Assert.Contains(
            "bess_optimization_run_duration_seconds_sum{asset_id=\"asset-1\",status=\"optimal\"} 0.5",
            scrape, StringComparison.Ordinal);
        Assert.Contains(
            string.Format(CultureInfo.InvariantCulture, "bess_optimization_objective_value{{asset_id=\"asset-1\"}} {0}", -1234.5),
            scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_run_with_violations_bumps_violation_counter_and_uses_failed_status_label()
    {
        var (metrics, registry) = Build();
        metrics.Record(BuildRun(
            assetId: "asset-2",
            status: OptimizationSolverStatus.Failed,
            solverRuntime: TimeSpan.FromMilliseconds(50),
            objectiveValue: 0,
            constraintViolations: FailedRunViolations));

        var scrape = await ScrapeAsync(registry);
        Assert.Contains(
            "bess_optimization_runs_total{asset_id=\"asset-2\",status=\"failed\"} 1",
            scrape, StringComparison.Ordinal);
        Assert.Contains(
            "bess_optimization_constraint_violations_total{asset_id=\"asset-2\"} 2",
            scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_without_violations_leaves_violation_counter_unset()
    {
        var (metrics, registry) = Build();
        metrics.Record(BuildRun(
            assetId: "asset-3",
            status: OptimizationSolverStatus.Optimal,
            solverRuntime: TimeSpan.FromMilliseconds(10),
            objectiveValue: -5));

        var scrape = await ScrapeAsync(registry);
        // Counter without an Inc() call must not appear with an asset-3
        // label set — prometheus-net only emits time-series the user has
        // touched.
        Assert.DoesNotContain(
            "bess_optimization_constraint_violations_total{asset_id=\"asset-3\"}",
            scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task All_seven_solver_status_labels_use_snake_case_wire_form()
    {
        var (metrics, registry) = Build();
        var statuses = new[]
        {
            (OptimizationSolverStatus.Optimal, "optimal"),
            (OptimizationSolverStatus.Feasible, "feasible"),
            (OptimizationSolverStatus.Infeasible, "infeasible"),
            (OptimizationSolverStatus.Unbounded, "unbounded"),
            (OptimizationSolverStatus.TimeLimit, "time_limit"),
            (OptimizationSolverStatus.IterationLimit, "iteration_limit"),
            (OptimizationSolverStatus.Failed, "failed"),
        };
        foreach (var (status, _) in statuses)
        {
            metrics.Record(BuildRun(
                assetId: "asset-status",
                status: status,
                solverRuntime: TimeSpan.FromMilliseconds(1),
                objectiveValue: 0));
        }

        var scrape = await ScrapeAsync(registry);
        foreach (var (_, wire) in statuses)
        {
            Assert.Contains(
                $"bess_optimization_runs_total{{asset_id=\"asset-status\",status=\"{wire}\"}} 1",
                scrape, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Null_run_throws()
    {
        var (metrics, _) = Build();
        Assert.Throws<ArgumentNullException>(() => metrics.Record(null!));
    }

    private static (PrometheusOptimizationRunMetrics Metrics, CollectorRegistry Registry) Build()
    {
        var registry = Metrics.NewCustomRegistry();
        return (new PrometheusOptimizationRunMetrics(registry), registry);
    }

    private static async Task<string> ScrapeAsync(CollectorRegistry registry)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static OptimizationRun BuildRun(
        string assetId,
        OptimizationSolverStatus status,
        TimeSpan solverRuntime,
        double objectiveValue,
        IReadOnlyList<string>? constraintViolations = null)
    {
        var hasSolution = status is OptimizationSolverStatus.Optimal or OptimizationSolverStatus.Feasible;
        var produced = hasSolution
            ? new ScheduleReference(assetId, ScheduleType.DayAhead, 1)
            : null;
        return new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: assetId,
            solverName: "spy-solver",
            status: status,
            horizonStart: HorizonStart,
            horizonEnd: HorizonStart + TimeSpan.FromHours(1),
            timeStep: TimeSpan.FromHours(1),
            objectiveValue: objectiveValue,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: constraintViolations ?? Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: solverRuntime,
            terminationReason: "test",
            createdAt: HorizonStart,
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: produced);
    }
}
