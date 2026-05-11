using BatteryEms.Domain;
using Grpc.Core;

namespace BatteryEms.Adapters.OptimizationCore;

// Plan-RM-M5-01-A D-04: Mapper-Implementation der versionierten
// `transport-mapping-v1.md`-Tabelle. Mappt gRPC-Status-Codes plus
// Sidecar-Payload (`solver_status` + `has_usable_solution`) auf die
// normierten Outcomes der plan-RM-M5 §Sidecar-Status-Taxonomie.
//
// **Source-of-Truth:** `proto/optimization-core/v1/transport-mapping-v1.md`.
// Jede Mapping-Änderung verlangt einen Plan-Slice und ein synchrones
// Doku-Update — die hier hartcodierte Logik ist 1:1-Spiegel.
//
// Worker konsumiert das `OptimizationCoreOutcome`-Record und entscheidet
// damit:
//   - `OptimizationRun.Status` (M2-Modell)
//   - `fallback_source` + `fallback_reason` Metric-Tags
//   - ProducedSchedule persistieren (`PersistSchedule=true`) oder nicht
public static class OptimizationCoreStatusMapper
{
    // Klassifikation einer Sidecar-Antwort (gRPC-OK + Payload).
    public static OptimizationCoreOutcome ClassifyResult(
        Grpc.V1.OptimizeResult.Types.SolverStatus solverStatus,
        bool hasUsableSolution)
    {
        return solverStatus switch
        {
            Grpc.V1.OptimizeResult.Types.SolverStatus.Optimal when hasUsableSolution =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Optimal,
                    FallbackSource: FallbackSource.SidecarResult,
                    FallbackReason: FallbackReason.None,
                    PersistSchedule: true),

            Grpc.V1.OptimizeResult.Types.SolverStatus.Feasible when hasUsableSolution =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Feasible,
                    FallbackSource: FallbackSource.SidecarResult,
                    FallbackReason: FallbackReason.None,
                    PersistSchedule: true),

            Grpc.V1.OptimizeResult.Types.SolverStatus.Infeasible =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Infeasible,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.SolverInfeasible,
                    PersistSchedule: false),

            Grpc.V1.OptimizeResult.Types.SolverStatus.Unbounded =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Unbounded,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.SolverUnbounded,
                    PersistSchedule: false),

            // TIME_LIMIT mit nutzbarer Zwischenlösung → als Feasible
            // persistieren, Termination-Code trägt die Quelle.
            Grpc.V1.OptimizeResult.Types.SolverStatus.TimeLimit when hasUsableSolution =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Feasible,
                    FallbackSource: FallbackSource.SidecarResult,
                    FallbackReason: FallbackReason.None,
                    PersistSchedule: true),

            Grpc.V1.OptimizeResult.Types.SolverStatus.TimeLimit =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.TimeLimit,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.SolverTimeLimit,
                    PersistSchedule: false),

            Grpc.V1.OptimizeResult.Types.SolverStatus.IterationLimit when hasUsableSolution =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Feasible,
                    FallbackSource: FallbackSource.SidecarResult,
                    FallbackReason: FallbackReason.None,
                    PersistSchedule: true),

            Grpc.V1.OptimizeResult.Types.SolverStatus.IterationLimit =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.IterationLimit,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.SolverIterationLimit,
                    PersistSchedule: false),

            // FAILED + jeder unbekannte SolverStatus
            // (`SOLVER_STATUS_UNSPECIFIED`, künftige Enum-Werte mit
            // älterem Worker): konservativ als TransportInternalError.
            _ =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.TransportInternalError,
                    PersistSchedule: false),
        };
    }

    // Klassifikation eines gRPC-Status-Codes (Wire-Layer-Outcome,
    // kein Sidecar-Payload verfügbar). Wird genutzt wenn der Call mit
    // einem nicht-`OK`-Status terminiert.
    public static OptimizationCoreOutcome ClassifyTransport(StatusCode code)
    {
        return code switch
        {
            StatusCode.OK =>
                throw new ArgumentException(
                    "ClassifyTransport must not be called for OK. Use "
                    + "ClassifyResult with the Sidecar payload instead.",
                    nameof(code)),

            StatusCode.DeadlineExceeded =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.TimeLimit,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.DeadlineExceeded,
                    PersistSchedule: false),

            StatusCode.Unavailable =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.SidecarUnavailable,
                    PersistSchedule: false),

            StatusCode.Cancelled =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.TransportCancelled,
                    PersistSchedule: false),

            StatusCode.InvalidArgument =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    // Plan-RM-M5 §Fallback-Matrix: InvalidArgument darf
                    // KEINEN lokalen Optimierer-Fallback mit denselben
                    // ungültigen Eingaben triggern.
                    FallbackSource: FallbackSource.NoActivation,
                    FallbackReason: FallbackReason.InvalidRequest,
                    PersistSchedule: false),

            StatusCode.Unauthenticated or StatusCode.PermissionDenied =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.UnauthorizedClient,
                    PersistSchedule: false),

            // Alle übrigen gRPC-Codes (Internal/Unknown/Aborted/usw.)
            // → konservativ als TransportInternalError.
            _ =>
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    FallbackSource: FallbackSource.FromMatrix,
                    FallbackReason: FallbackReason.TransportInternalError,
                    PersistSchedule: false),
        };
    }

    // Pre-Request-Gate-Outcome: Contract-Version-Mismatch oder
    // Pflicht-Feature-Flag fehlt. Wird ohne Sidecar-Call gemappt.
    public static OptimizationCoreOutcome ClassifyContractIncompatible() =>
        new(
            Status: OptimizationSolverStatus.Failed,
            FallbackSource: FallbackSource.NoActivation,
            FallbackReason: FallbackReason.ContractIncompatible,
            PersistSchedule: false);
}

// Klassifikations-Ergebnis. `Status` ist die M2-OptimizationRun-
// Status-Zelle; `FallbackSource`/`FallbackReason` die normierten
// Metric-Tags aus plan-RM-M5 §Fallback-Taxonomie; `PersistSchedule`
// dirigiert die Worker-Side-Logik (true ⇒ ProducedSchedule schreiben,
// false ⇒ keine neue Schedule-Version, Fallback-Matrix anwenden).
public sealed record OptimizationCoreOutcome(
    OptimizationSolverStatus Status,
    FallbackSource FallbackSource,
    FallbackReason FallbackReason,
    bool PersistSchedule);

// Plan-RM-M5 §Fallback-Taxonomie: kanonische `fallback_source`-Werte.
// `FromMatrix` markiert den Fall „Worker entscheidet anhand der
// Fallback-Matrix (lokaler Optimierer vs Safe-Stop)". Worker resolviert
// das beim Apply.
public enum FallbackSource
{
    None,
    SidecarResult,
    FromMatrix,
    LocalOptimizer,
    LastValidSchedule,
    SafeStop,
    NoActivation,
}

// Plan-RM-M5 §Fallback-Taxonomie: kanonische `fallback_reason`-Werte.
public enum FallbackReason
{
    None,
    DeadlineExceeded,
    SidecarUnavailable,
    TransportCancelled,
    TransportInternalError,
    InvalidRequest,
    SolverInfeasible,
    SolverUnbounded,
    SolverTimeLimit,
    SolverIterationLimit,
    NoValidPlan,
    FallbackPlanExpired,
    FallbackContextMismatch,
    FallbackTelemetryDrift,
    InvalidSnapshot,
    InvalidMpcState,
    ContractIncompatible,
    UnauthorizedClient,
    DuplicateRequest,
    LateResponseIgnored,
}
