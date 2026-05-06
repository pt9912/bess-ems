using BatteryEms.Application.Observability;
using Prometheus;

namespace BatteryEms.Adapters.Telemetry.Prometheus;

// Maps the Application-side IControlCycleMetrics calls onto prometheus-
// net instruments. All metrics carry an 'asset_id' label so dashboards
// can split by site; histograms use buckets tuned for a 1-Hz regulation
// loop (LH-CTRL-005) and bounded snapshot/command latencies.
public sealed class PrometheusControlCycleMetrics : IControlCycleMetrics
{
    private static readonly string[] AssetIdLabels = { "asset_id" };
    private static readonly string[] AssetReasonLabels = { "asset_id", "reason" };
    private static readonly string[] AssetComponentLabels = { "asset_id", "component" };
    // 1 ms .. 5 s — covers fast paths (operator-stop) and slow optimiser
    // worst case in one histogram.
    private static readonly double[] DurationBuckets = { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0 };

    private readonly Histogram _cycleDuration;
    private readonly Counter _invalidSnapshots;
    private readonly Counter _communicationErrors;
    private readonly Histogram _commandLatency;
    private readonly Gauge _activePowerKw;
    private readonly Gauge _socPercent;
    private readonly Counter _safeStops;

    public PrometheusControlCycleMetrics()
        : this(Metrics.DefaultRegistry)
    {
    }

    public PrometheusControlCycleMetrics(CollectorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var factory = Metrics.WithCustomRegistry(registry);

        _cycleDuration = factory.CreateHistogram(
            "bess_control_cycle_duration_seconds",
            "Wall-clock duration of one control cycle (LH-MON-002 'Regelzyklusdauer').",
            new HistogramConfiguration
            {
                LabelNames = AssetIdLabels,
                Buckets = DurationBuckets,
            });

        _invalidSnapshots = factory.CreateCounter(
            "bess_invalid_snapshots_total",
            "Snapshots missing or rejected by IsUsableForControl (LH-MON-002 'ungültige Snapshots').",
            new CounterConfiguration { LabelNames = AssetReasonLabels });

        _communicationErrors = factory.CreateCounter(
            "bess_communication_errors_total",
            "Adapter-level transport failures (Modbus/MQTT). Wired with RM-M1-19.",
            new CounterConfiguration { LabelNames = AssetComponentLabels });

        _commandLatency = factory.CreateHistogram(
            "bess_command_latency_seconds",
            "Latency from snapshot timestamp to command timestamp (LH-MON-002 'Command-Latenz').",
            new HistogramConfiguration
            {
                LabelNames = AssetIdLabels,
                Buckets = DurationBuckets,
            });

        _activePowerKw = factory.CreateGauge(
            "bess_active_power_kw",
            "Latest active-power setpoint in kW (positive = discharge, negative = charge).",
            new GaugeConfiguration { LabelNames = AssetIdLabels });

        _socPercent = factory.CreateGauge(
            "bess_soc_percent",
            "Latest observed state of charge in percent (LH-MON-002 'SOC').",
            new GaugeConfiguration { LabelNames = AssetIdLabels });

        _safeStops = factory.CreateCounter(
            "bess_safe_stops_total",
            "Safe-stop emissions per asset and reason — proxy for solver/health status.",
            new CounterConfiguration { LabelNames = AssetReasonLabels });
    }

    public void RecordCycleDuration(string assetId, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        _cycleDuration.WithLabels(assetId).Observe(duration.TotalSeconds);
    }

    public void IncrementInvalidSnapshot(string assetId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _invalidSnapshots.WithLabels(assetId, reason).Inc();
    }

    public void IncrementCommunicationError(string assetId, string component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        _communicationErrors.WithLabels(assetId, component).Inc();
    }

    public void RecordCommandLatency(string assetId, TimeSpan latency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        _commandLatency.WithLabels(assetId).Observe(latency.TotalSeconds);
    }

    public void SetActivePowerKw(string assetId, double valueKw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        _activePowerKw.WithLabels(assetId).Set(valueKw);
    }

    public void SetSocPercent(string assetId, double valuePercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        _socPercent.WithLabels(assetId).Set(valuePercent);
    }

    public void RecordSafeStop(string assetId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _safeStops.WithLabels(assetId, reason).Inc();
    }
}
