namespace BatteryEms.Application.Observability;

// LH-MON-002: technical and functional metrics for the control loop.
// This port stays framework-free so the Application layer can drive it
// without dragging prometheus-net (or any other backend) inward; the
// driven adapter Adapters.Telemetry maps the calls onto Prometheus
// instruments. Tests use NoOpControlCycleMetrics or a hand-rolled spy.
public interface IControlCycleMetrics
{
    // Wall-clock duration of one ExecuteAsync call. Always recorded,
    // even on the SafeStop fast paths, so the histogram captures the
    // tail of the regulation cycle (LH-MON-002 "Regelzyklusdauer").
    void RecordCycleDuration(string assetId, TimeSpan duration);

    // Snapshots that were missing or rejected by IsUsableForControl.
    // Reason carries the DataQuality reason or the synthetic codes
    // 'no-snapshot' / 'asset-unavailable' for downstream alerting.
    void IncrementInvalidSnapshot(string assetId, string reason);

    // Adapter-level transport failures (Modbus/MQTT). The control-cycle
    // use case does not call this directly — the wiring lands with the
    // Worker composition root in RM-M1-19 — but the port is stable now.
    void IncrementCommunicationError(string assetId, string component);

    // Latency between the snapshot timestamp and the command timestamp,
    // i.e. how stale the input was when the cycle reached its decision.
    // Captures LH-MON-002 "Command-Latenz".
    void RecordCommandLatency(string assetId, TimeSpan latency);

    // Last accepted active-power setpoint. Negative = charge, positive
    // = discharge (Domain.SignConvention). Useful for grafana panels
    // showing the live dispatch.
    void SetActivePowerKw(string assetId, double valueKw);

    // Latest observed SOC (LH-MON-002 "SOC").
    void SetSocPercent(string assetId, double valuePercent);

    // SafeStop emissions broken out by reason so dashboards can show
    // the dominant trigger without rebuilding the histogram on the fly.
    void RecordSafeStop(string assetId, string reason);
}
