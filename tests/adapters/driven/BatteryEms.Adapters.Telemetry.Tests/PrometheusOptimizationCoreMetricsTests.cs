using System.IO;
using System.Text;
using BatteryEms.Adapters.Telemetry.Prometheus;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Prometheus;
using Xunit;

namespace BatteryEms.Adapters.Telemetry.Tests;

public sealed class PrometheusOptimizationCoreMetricsTests
{
    [Fact]
    public async Task Successful_sidecar_run_scrapes_status_fallback_terminal_and_duration()
    {
        var (metrics, registry) = Build();

        metrics.RecordRun(
            "asset-1",
            OptimizationSolverStatus.Optimal,
            "sidecar_result",
            "none",
            OptimizationTerminalState.SidecarCommitted,
            TimeSpan.FromMilliseconds(25));

        var scrape = await ScrapeAsync(registry);
        const string Labels = "asset_id=\"asset-1\",status=\"optimal\",fallback_source=\"sidecar_result\",fallback_reason=\"none\",terminal_state=\"sidecar_committed\"";
        Assert.Contains($"bess_optimization_core_runs_total{{{Labels}}} 1", scrape, StringComparison.Ordinal);
        Assert.Contains($"bess_optimization_core_run_duration_seconds_count{{{Labels}}} 1", scrape, StringComparison.Ordinal);
        Assert.Contains($"bess_optimization_core_run_duration_seconds_sum{{{Labels}}} 0.025", scrape, StringComparison.Ordinal);
        Assert.Contains($"bess_optimization_core_terminal_states_total{{{Labels}}} 1", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_deadline_run_scrapes_fallback_reason_and_failed_terminal_state()
    {
        var (metrics, registry) = Build();

        metrics.RecordRun(
            "asset-2",
            OptimizationSolverStatus.TimeLimit,
            "no_activation",
            "deadline_exceeded",
            OptimizationTerminalState.FailedNoActivation,
            TimeSpan.FromSeconds(2));

        var scrape = await ScrapeAsync(registry);
        const string Labels = "asset_id=\"asset-2\",status=\"time_limit\",fallback_source=\"no_activation\",fallback_reason=\"deadline_exceeded\",terminal_state=\"failed_no_activation\"";
        Assert.Contains($"bess_optimization_core_runs_total{{{Labels}}} 1", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sidecar_health_status_scrapes_last_observed_state_label()
    {
        var (metrics, registry) = Build();

        metrics.RecordSidecarHealth("serving");
        metrics.RecordSidecarHealth("unavailable");

        var scrape = await ScrapeAsync(registry);
        Assert.Contains("bess_optimization_core_sidecar_health_status{status=\"serving\"} 0", scrape, StringComparison.Ordinal);
        Assert.Contains("bess_optimization_core_sidecar_health_status{status=\"unavailable\"} 1", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_inputs_throw()
    {
        var (metrics, _) = Build();

        Assert.Throws<ArgumentException>(() => metrics.RecordRun(
            "",
            OptimizationSolverStatus.Optimal,
            "sidecar_result",
            "none",
            OptimizationTerminalState.SidecarCommitted,
            TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => metrics.RecordRun(
            "asset-1",
            OptimizationSolverStatus.Optimal,
            "",
            "none",
            OptimizationTerminalState.SidecarCommitted,
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.RecordRun(
            "asset-1",
            OptimizationSolverStatus.Optimal,
            "sidecar_result",
            "none",
            OptimizationTerminalState.SidecarCommitted,
            TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentException>(() => metrics.RecordSidecarHealth(""));
    }

    private static (PrometheusOptimizationCoreMetrics Metrics, CollectorRegistry Registry) Build()
    {
        var registry = Metrics.NewCustomRegistry();
        return (new PrometheusOptimizationCoreMetrics(registry), registry);
    }

    private static async Task<string> ScrapeAsync(CollectorRegistry registry)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
