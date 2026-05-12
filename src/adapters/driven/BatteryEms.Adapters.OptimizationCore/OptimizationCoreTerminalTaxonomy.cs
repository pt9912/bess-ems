using BatteryEms.Application.Optimization;

namespace BatteryEms.Adapters.OptimizationCore;

internal static class OptimizationCoreTerminalTaxonomy
{
    public static string MapTerminalReason(FallbackReason reason) => reason switch
    {
        FallbackReason.None => "none",
        FallbackReason.DeadlineExceeded => "deadline-exceeded",
        FallbackReason.SidecarUnavailable => "sidecar-unavailable",
        FallbackReason.TransportCancelled => "transport-cancelled",
        FallbackReason.TransportInternalError => "transport-internal-error",
        FallbackReason.InvalidRequest => "invalid-request",
        FallbackReason.SolverInfeasible => "solver-infeasible",
        FallbackReason.SolverUnbounded => "solver-unbounded",
        FallbackReason.SolverTimeLimit => "solver-time-limit",
        FallbackReason.SolverIterationLimit => "solver-iteration-limit",
        FallbackReason.NoValidPlan => "no-valid-plan",
        FallbackReason.FallbackPlanExpired => "fallback-plan-expired",
        FallbackReason.FallbackContextMismatch => "fallback-context-mismatch",
        FallbackReason.FallbackTelemetryDrift => "fallback-telemetry-drift",
        FallbackReason.InvalidSnapshot => "invalid-snapshot",
        FallbackReason.InvalidMpcState => "invalid-mpc-state",
        FallbackReason.ContractIncompatible => "contract-incompatible",
        FallbackReason.UnauthorizedClient => "unauthorized-client",
        FallbackReason.DuplicateRequest => "duplicate-request",
        FallbackReason.LateResponseIgnored => "late-response-ignored",
        _ => "transport-internal-error",
    };

    public static string MapFallbackSource(FallbackSource source) => source switch
    {
        FallbackSource.None => "none",
        FallbackSource.SidecarResult => "sidecar_result",
        FallbackSource.LocalOptimizer => "local_optimizer",
        FallbackSource.LastValidSchedule => "last_valid_schedule",
        FallbackSource.SafeStop => "safe_stop",
        FallbackSource.NoActivation => "no_activation",
        FallbackSource.FromMatrix => "no_activation",
        _ => "no_activation",
    };

    public static string MapFallbackReason(FallbackReason reason) => reason switch
    {
        FallbackReason.None => "none",
        FallbackReason.DeadlineExceeded => "deadline_exceeded",
        FallbackReason.SidecarUnavailable => "sidecar_unavailable",
        FallbackReason.TransportCancelled => "transport_cancelled",
        FallbackReason.TransportInternalError => "transport_internal_error",
        FallbackReason.InvalidRequest => "invalid_request",
        FallbackReason.SolverInfeasible => "solver_infeasible",
        FallbackReason.SolverUnbounded => "solver_unbounded",
        FallbackReason.SolverTimeLimit => "solver_time_limit",
        FallbackReason.SolverIterationLimit => "solver_iteration_limit",
        FallbackReason.NoValidPlan => "no_valid_plan",
        FallbackReason.FallbackPlanExpired => "fallback_plan_expired",
        FallbackReason.FallbackContextMismatch => "fallback_context_mismatch",
        FallbackReason.FallbackTelemetryDrift => "fallback_telemetry_drift",
        FallbackReason.InvalidSnapshot => "invalid_snapshot",
        FallbackReason.InvalidMpcState => "invalid_mpc_state",
        FallbackReason.ContractIncompatible => "contract_incompatible",
        FallbackReason.UnauthorizedClient => "unauthorized_client",
        FallbackReason.DuplicateRequest => "duplicate_request",
        FallbackReason.LateResponseIgnored => "late_response_ignored",
        _ => "transport_internal_error",
    };

    public static FallbackSource ResolveFallbackSource(
        FallbackSource source,
        OptimizationTerminalState terminalState)
    {
        if (source != FallbackSource.FromMatrix)
        {
            return source;
        }

        return terminalState switch
        {
            OptimizationTerminalState.FallbackCommitted => FallbackSource.LocalOptimizer,
            OptimizationTerminalState.SidecarCommitted => FallbackSource.SidecarResult,
            _ => FallbackSource.NoActivation,
        };
    }

    public static FallbackReason ResolveFallbackReasonForBuiltResult(
        OptimizationCoreOutcome outcome,
        ScheduleOptimizationResult result)
    {
        if (outcome.PersistSchedule && result.ProducedSchedule is null)
        {
            return FallbackReason.TransportInternalError;
        }

        return outcome.FallbackReason;
    }
}
