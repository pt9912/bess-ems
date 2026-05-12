using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Plan-RM-M5 §Fallback-Plan-Gueltigkeit: validiert einen Schedule-
// Kandidaten BEVOR der Worker ihn als Fallback wiederverwendet.
// Vier orthogonale Achsen:
//   1. Zeitindex — aktueller Tick liegt im halboffenen
//      Schedule-Horizon?
//   2. MaxFallbackScheduleAge — current_tick_utc - schedule_created_at_utc
//      <= konfigurierte Schwelle (Default `min(Schedule.TimeStep,
//      2 * ControlCycleInterval)` pro Asset/ScheduleType)?
//   3. Kontext-Stempel — asset_id / schedule_type / horizon /
//      time_step / market_bid_area decken sich mit dem Use-Case?
//   4. Telemetrie-Drift — aktuelle Telemetrie liegt im Bereich, mit
//      dem der Plan erzeugt wurde (Asset-Bounds)?
//
// Jede Invalidation liefert einen maschinenlesbaren Reason aus der
// FallbackReason-Taxonomie. Aufrufer (Worker im Control-Cycle bei
// Sidecar-/Solver-Fehler) entscheidet dann: lokaler Optimierer-
// Fallback, last-valid-Schedule beibehalten, oder Safe-Stop mit
// `no_valid_plan`.
public interface IFallbackPlanValidator
{
    FallbackPlanValidationResult Validate(
        FallbackPlanCandidate candidate,
        FallbackPlanContext context);
}

// Bündelt einen Schedule + Erstellungs-Zeitpunkt. Schedule-Domain
// trägt heute keine CreatedAt-Property; die kommt aus dem erzeugenden
// `OptimizationRun.CreatedAt`.
public sealed record FallbackPlanCandidate(
    Schedule Schedule,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, string>? MpcStamps = null);

// Use-Case-Kontext gegen den der Kandidat verglichen wird.
public sealed record FallbackPlanContext(
    string AssetId,
    ScheduleType ScheduleType,
    DateTimeOffset CurrentTickUtc,
    DateTimeOffset HorizonStart,
    DateTimeOffset HorizonEnd,
    TimeSpan TimeStep,
    string MarketBidArea,
    BatteryAsset Asset,
    BatteryTelemetry? CurrentTelemetry,
    IReadOnlyDictionary<string, string>? MpcStamps = null);

// Outcome-Record. `IsValid==true` ⇒ Kandidat darf als Fallback
// verwendet werden; sonst trägt `Reason` den maschinenlesbaren
// Invalidations-Grund (FallbackReason-Taxonomie aus plan-RM-M5
// §Fallback-Taxonomie).
public sealed record FallbackPlanValidationResult(
    bool IsValid,
    FallbackReason Reason,
    string? Detail)
{
    public static readonly FallbackPlanValidationResult Valid =
        new(true, FallbackReason.None, null);

    public static FallbackPlanValidationResult Invalid(
        FallbackReason reason, string detail) =>
        new(false, reason, detail);
}

// FallbackReason-Subset für den Validator (1:1 aus plan-RM-M5
// §Fallback-Taxonomie). Separater Enum-Name verhindert eine
// Kollision mit dem adapter-lokalen `FallbackReason` im
// OptimizationCoreStatusMapper (M5-D-Slice konsolidiert beide
// auf einen Domain-Enum, sobald Wiring-Bedarf konkret wird).
public enum FallbackReason
{
    // Kein Plan-Validitäts-Problem.
    None,

    // Außerhalb Horizon ODER älter als MaxFallbackScheduleAge.
    FallbackPlanExpired,

    // Kontext-Stempel passt nicht (asset_id / schedule_type /
    // time_step / market_bid_area).
    FallbackContextMismatch,

    // Aktuelle Telemetrie liegt außerhalb der Asset-Bounds, mit
    // denen der Plan erzeugt wurde.
    FallbackTelemetryDrift,

    // Kein Kandidat verfügbar (Caller-Side; Worker hat keinen
    // alten Plan im Repository).
    NoValidPlan,
}
