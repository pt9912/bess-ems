using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driven port for the persistent Regelleistung activation dedupe
// tracker (plan-RM-M4-03 §145). The store is keyed on the identity
// tuple (SourceId, ActivationId); replay detection compares
// PayloadHash for ON-CONFLICT hits.
//
// The store itself emits four of the five AcceptResult variants —
// Accepted, ReplayIdempotent, RejectedDedupeConflict,
// RejectedDedupeStoreInvalid. RejectedAmbiguousDuplicate is reserved
// for the use-case/dispatcher (Sub-Slice D/E) which composes
// dedupe-store results across concurrent candidates and surfaces the
// degenerate-tiebreak case.
public interface IActivationDedupeStore
{
    Task<AcceptResult> TryAcceptAsync(
        RegelleistungActivation activation,
        CancellationToken cancellationToken = default);

    // Read-only health surface for the /health/regelleistung endpoint
    // (Sub-Slice D) and the production-gate pre-condition. True after
    // any of the four tracker-load fail-closed sub-cases (a/b/c/d)
    // has fired; remains true until the operator triggers an explicit
    // recovery (Dapper variant) or in-memory recover (test variant).
    bool IsInvalid { get; }
}

// Outcome of a dedupe-store accept attempt. Plan §145 enumerates the
// five variants as a single enum even though the store itself only
// emits four — keeping them grouped lets the use-case layer return
// the same union type without an additional adapter.
public enum AcceptResult
{
    // The activation was newly stored; downstream pipeline may
    // continue to dispatch evaluation.
    Accepted,

    // Same identity (SourceId, ActivationId) and same PayloadHash as
    // a previously accepted entry — idempotent retry, no state
    // change. Pipeline treats this as a benign no-op.
    ReplayIdempotent,

    // Same identity (SourceId, ActivationId) but the PayloadHash
    // differs from the stored one. Either a buggy source or a replay
    // attack with mutated payload — fail-closed.
    RejectedDedupeConflict,

    // Two competing candidates landed at the same dispatch tick with
    // identical tiebreak rank (Sub-Slice E). Reserved for the
    // use-case/dispatcher; the dedupe-store TryAccept never returns
    // this directly.
    RejectedAmbiguousDuplicate,

    // The store has detected a load/state-validation failure and
    // refuses to accept until externally recovered. The four master-
    // DoD sub-cases (incompatible checkpoint, oversize per-source
    // count, partially-corrupt entry, parse/decode error) all funnel
    // here. Pipeline marks the activation not dispatch-relevant
    // until the operator clears the underlying issue.
    RejectedDedupeStoreInvalid,
}
