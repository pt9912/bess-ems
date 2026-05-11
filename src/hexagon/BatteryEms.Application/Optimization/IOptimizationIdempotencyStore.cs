namespace BatteryEms.Application.Optimization;

// Plan-RM-M5 §Request-Idempotenz Und Retry: worker-owned Idempotency-
// Store. Pro `request_id` höchstens ein OptimizationRun und höchstens
// eine Schedule-Version. Sidecar bleibt für Aktivierungseffekte
// stateless; der Worker führt den Store in derselben persistierten
// Datenbank wie OptimizationRun/Schedule-Versionen.
//
// Atomar via Compare-and-Set: `TryFinalizeAsync` gewinnt die erste
// erfolgreiche Transition `Pending → <Terminal>`; alle späteren
// Aufrufe lesen den vorhandenen Terminalzustand und dürfen keine
// zweite Aktivierung auslösen (`late_response_ignored`-Pfad).
//
// Worker-Restart-Recovery: nach Restart darf dieselbe `request_id`
// nur anhand des persistierten Eintrags fortgesetzt oder als
// Duplicate verworfen werden (siehe `TryBeginAsync`).
public interface IOptimizationIdempotencyStore
{
    // Atomar: legt einen Pending-Eintrag für `requestId` an wenn
    // keiner existiert, oder gibt den existierenden Eintrag zurück.
    // Aufrufer prüft `IsNewlyCreated`: false bedeutet ein vorhandener
    // (möglicherweise bereits finalisierter) Eintrag.
    Task<OptimizationIdempotencyBeginResult> TryBeginAsync(
        string requestId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    // Atomar: transitioniert von `Pending` zum konkreten Terminal-
    // Zustand. Liefert `true` wenn die Transition stattgefunden hat;
    // `false` wenn der Eintrag bereits final ist (anderer Aufrufer
    // hat den CAS gewonnen, oder der Eintrag wurde aus einem anderen
    // Prozess finalisiert).
    Task<bool> TryFinalizeAsync(
        string requestId,
        OptimizationTerminalState terminalState,
        string terminalReason,
        Guid? runId,
        int? producedVersion,
        DateTimeOffset committedAt,
        CancellationToken cancellationToken);

    // Liest den aktuellen Zustand. Null wenn keine `request_id`
    // existiert.
    Task<OptimizationIdempotencyEntry?> ReadAsync(
        string requestId,
        CancellationToken cancellationToken);
}

// Outcome von `TryBeginAsync`: entweder ein frisch angelegter
// Pending-Eintrag oder ein existierender (möglicherweise bereits
// final). `Entry` ist nie null.
public sealed record OptimizationIdempotencyBeginResult(
    OptimizationIdempotencyEntry Entry,
    bool IsNewlyCreated);

// Per-Request-Audit-Record im Idempotency-Store.
public sealed record OptimizationIdempotencyEntry(
    string RequestId,
    OptimizationTerminalState TerminalState,
    string TerminalReason,
    Guid? RunId,
    int? ProducedVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CommittedAt)
{
    public bool IsFinal => TerminalState != OptimizationTerminalState.Pending;
}

// Plan-RM-M5 §Fallback-Taxonomie: atomare Terminalzustände pro
// `request_id`. Genau einer pro Lifetime; spätere Transitionsversuche
// lesen den vorhandenen Zustand und dürfen keine zweite Aktivierung
// auslösen.
public enum OptimizationTerminalState
{
    // Initialer Zustand nach `TryBeginAsync` — die Optimierung läuft
    // gerade. Wird per `TryFinalizeAsync` auf einen Terminal-Wert
    // gesetzt.
    Pending,

    // Sidecar hat eine usable Lösung geliefert; Worker hat das
    // produzierte Schedule persistiert.
    SidecarCommitted,

    // Sidecar-Pfad ist gescheitert (Deadline/Unavailable/usw.); ein
    // lokaler Fallback-Optimierer hat eine Lösung geliefert die
    // persistiert wurde.
    FallbackCommitted,

    // Operator hat den Call abgebrochen, oder eine Cancellation-Quelle
    // hat ihn beendet. Keine Aktivierung.
    Cancelled,

    // Sidecar UND Fallback haben beide gescheitert ohne Schedule-
    // Version. Worker bleibt auf dem letzten gültigen Plan oder
    // Safe-Stop.
    FailedNoActivation,

    // Späte Sidecar-Antwort für eine bereits-finalisierte
    // `request_id`. Wird observability-mäßig markiert aber bewegt
    // den Zustand nicht weiter.
    LateResponseIgnored,
}
