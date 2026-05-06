using System.Globalization;
using System.IO;
using System.Text;
using BatteryEms.Adapters.Telemetry.Prometheus;
using Prometheus;
using Xunit;

namespace BatteryEms.Adapters.Telemetry.Tests;

public sealed class PrometheusControlCycleMetricsTests
{
    [Fact]
    public async Task Cycle_duration_observation_lands_in_histogram_with_asset_label()
    {
        var (metrics, registry) = Build();
        metrics.RecordCycleDuration("asset-1", TimeSpan.FromMilliseconds(7));

        var scrape = await ScrapeAsync(registry);
        Assert.Contains("bess_control_cycle_duration_seconds_count{asset_id=\"asset-1\"} 1", scrape, StringComparison.Ordinal);
        Assert.Contains("bess_control_cycle_duration_seconds_sum{asset_id=\"asset-1\"} 0.007", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_snapshot_counter_increments_per_asset_and_reason()
    {
        var (metrics, registry) = Build();
        metrics.IncrementInvalidSnapshot("asset-1", "no-snapshot");
        metrics.IncrementInvalidSnapshot("asset-1", "no-snapshot");
        metrics.IncrementInvalidSnapshot("asset-2", "snapshot-aged");

        var scrape = await ScrapeAsync(registry);
        Assert.Contains("bess_invalid_snapshots_total{asset_id=\"asset-1\",reason=\"no-snapshot\"} 2", scrape, StringComparison.Ordinal);
        Assert.Contains("bess_invalid_snapshots_total{asset_id=\"asset-2\",reason=\"snapshot-aged\"} 1", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Power_and_soc_gauges_track_latest_value()
    {
        var (metrics, registry) = Build();
        metrics.SetActivePowerKw("asset-1", 25);
        metrics.SetActivePowerKw("asset-1", -10);  // overwrites
        metrics.SetSocPercent("asset-1", 47.5);

        var scrape = await ScrapeAsync(registry);
        Assert.Contains("bess_active_power_kw{asset_id=\"asset-1\"} -10", scrape, StringComparison.Ordinal);
        Assert.Contains(
            string.Format(CultureInfo.InvariantCulture, "bess_soc_percent{{asset_id=\"asset-1\"}} {0}", 47.5),
            scrape,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Safe_stop_and_communication_error_counters_show_up_with_their_labels()
    {
        var (metrics, registry) = Build();
        metrics.RecordSafeStop("asset-1", "operator-stop:evac-drill");
        metrics.IncrementCommunicationError("asset-1", "modbus");

        var scrape = await ScrapeAsync(registry);
        Assert.Contains("bess_safe_stops_total{asset_id=\"asset-1\",reason=\"operator-stop:evac-drill\"} 1", scrape, StringComparison.Ordinal);
        Assert.Contains("bess_communication_errors_total{asset_id=\"asset-1\",component=\"modbus\"} 1", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_inputs_throw_argument_exception()
    {
        var (metrics, _) = Build();
        Assert.Throws<ArgumentException>(() => metrics.RecordCycleDuration("", TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => metrics.IncrementInvalidSnapshot("a", ""));
        Assert.Throws<ArgumentException>(() => metrics.IncrementCommunicationError("a", ""));
        Assert.Throws<ArgumentException>(() => metrics.RecordCommandLatency("", TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => metrics.SetActivePowerKw("", 0));
        Assert.Throws<ArgumentException>(() => metrics.SetSocPercent("", 0));
        Assert.Throws<ArgumentException>(() => metrics.RecordSafeStop("a", ""));
    }

    private static (PrometheusControlCycleMetrics Metrics, CollectorRegistry Registry) Build()
    {
        var registry = Metrics.NewCustomRegistry();
        return (new PrometheusControlCycleMetrics(registry), registry);
    }

    private static async Task<string> ScrapeAsync(CollectorRegistry registry)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
