using System.Diagnostics;

namespace BatteryEms.Application.Observability;

// RM-M2-06 / LH-MON-003: ActivitySources for the three flow boundaries
// the spec calls out as "kritische Abläufe":
//
//   ControlCycle           — wraps a per-asset tick (snapshot read,
//                             dispatch, limits, command emission).
//   CommandDispatch        — wraps the IBatteryCommandSink.WriteAsync
//                             call so failed dispatches surface in the
//                             trace as well as in metrics/logs.
//   ScheduleOptimization   — wraps the schedule-optimisation use case
//                             from the API trigger down through the
//                             solver call.
//
// These sources are System.Diagnostics primitives (BCL) — no
// OpenTelemetry SDK reference here. The Hexagon stays framework-free;
// the Adapters.Telemetry assembly subscribes to these source names via
// its OTel TracerProvider configuration.
//
// Span-attribute names follow LH-MON-001's structured-logging convention
// (asset_id, decision, reason) so dashboards and alerts can pivot on
// the same field set across logs, metrics and traces.
public static class BessActivitySources
{
    public const string ControlCycleName = "BatteryEms.ControlCycle";
    public const string CommandDispatchName = "BatteryEms.CommandDispatch";
    public const string ScheduleOptimizationName = "BatteryEms.ScheduleOptimization";

    public static readonly ActivitySource ControlCycle = new(ControlCycleName);
    public static readonly ActivitySource CommandDispatch = new(CommandDispatchName);
    public static readonly ActivitySource ScheduleOptimization = new(ScheduleOptimizationName);
}

// Span-attribute keys, kept as constants so the producer (Application)
// and the consumer (dashboards / alerts / RM-M2-06 tests) agree on
// names without scattering string literals.
public static class BessActivityTags
{
    public const string AssetId = "bess.asset_id";
    public const string Decision = "bess.decision";
    public const string CommandMode = "bess.command_mode";
    public const string PowerKw = "bess.power_kw";
    public const string CommandReason = "bess.command_reason";
    public const string DispatchSuccess = "bess.dispatch_success";
    public const string DispatchReason = "bess.dispatch_reason";
    public const string RunId = "bess.run_id";
    public const string SolverStatus = "bess.solver_status";
    public const string ProducedScheduleVersion = "bess.produced_schedule_version";
    public const string TerminationReason = "bess.termination_reason";
}
