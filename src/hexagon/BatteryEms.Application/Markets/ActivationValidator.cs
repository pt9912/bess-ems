using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Pipeline orchestrator for incoming Regelleistung activation signals
// (plan-RM-M4-03 §146). Runs the four DoD checks in this order:
//
//   (1) Source-/Payload-Schema (field format gate)
//   (2) UTC time-window via ActivationTimeValidator
//   (3) TimebaseDegraded gate (debounce state from the cycle owner)
//   (4) Dedupe via IActivationDedupeStore.TryAcceptAsync
//
// Dedupe is intentionally last so a replay-hit must still pass time
// validation and timebase-state checks first — a compromised source
// that resends an old (source_id, activation_id) with stale signal
// timestamp must not bypass the freshness gate just because the
// dedupe tracker still remembers the entry.
//
// The validator is async because step 4 hits the persistent dedupe
// store. Steps 1-3 are pure and resolve synchronously; the orchestrator
// short-circuits without touching the dedupe store on a failure in
// any earlier step. That keeps the InMemory test path deterministic
// without any DB round-trip when the activation is rejected before
// dedupe.
public sealed class ActivationValidator
{
    private readonly RegelleistungOptions _options;
    private readonly IActivationDedupeStore _dedupeStore;
    private readonly IClock _clock;

    public ActivationValidator(
        RegelleistungOptions options,
        IActivationDedupeStore dedupeStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dedupeStore);
        ArgumentNullException.ThrowIfNull(clock);
        options.EnsureValid();
        _options = options;
        _dedupeStore = dedupeStore;
        _clock = clock;
    }

    public async Task<ActivationValidationResult> ValidateAsync(
        RegelleistungActivation activation,
        TimebaseDebounceState timebaseState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(timebaseState);
        cancellationToken.ThrowIfCancellationRequested();

        // (1) Schema: identifier format gate. Construction blocks
        //     empty/whitespace IDs; this catches internal whitespace
        //     and control characters that a permissive source adapter
        //     might pass through.
        var schemaResult = ValidateSchema(activation);
        if (!schemaResult.IsAccepted)
        {
            return schemaResult;
        }

        // (2) UTC time window — pure Domain check.
        var timeResult = ActivationTimeValidator.Validate(activation, _clock.UtcNow, _options);
        if (!timeResult.IsAccepted)
        {
            return timeResult;
        }

        // (3) Timebase debounce state. While Degraded, no activation
        //     is admitted — the master DoD wording: "auch wenn die
        //     Aktivierung selbst Schema-konform ist, blockiert
        //     `TimebaseDegraded` jede Rezeption". The check sits before
        //     dedupe so a replay-hit doesn't silently pass while the
        //     timebase is unhealthy.
        if (timebaseState.Health == TimebaseHealth.Degraded)
        {
            return ActivationValidationResult.Reject(
                ActivationValidationReasons.TimebaseDegraded,
                "timebase debounce state is Degraded; activation cannot be admitted.");
        }

        // (4) Dedupe (always last). Maps the AcceptResult union to the
        //     pipeline's ActivationValidationResult shape so downstream
        //     consumers see a single result type from every step.
        var dedupeOutcome = await _dedupeStore.TryAcceptAsync(activation, cancellationToken).ConfigureAwait(false);
        return MapDedupeOutcome(dedupeOutcome);
    }

    private static ActivationValidationResult ValidateSchema(RegelleistungActivation activation)
    {
        if (ContainsWhitespaceOrControl(activation.SourceId))
        {
            return ActivationValidationResult.Reject(
                ActivationValidationReasons.SchemaInvalid,
                $"source_id contains whitespace or control characters.");
        }
        if (ContainsWhitespaceOrControl(activation.ActivationId))
        {
            return ActivationValidationResult.Reject(
                ActivationValidationReasons.SchemaInvalid,
                $"activation_id contains whitespace or control characters.");
        }
        if (ContainsWhitespaceOrControl(activation.PayloadHash))
        {
            return ActivationValidationResult.Reject(
                ActivationValidationReasons.SchemaInvalid,
                $"payload_hash contains whitespace or control characters.");
        }
        return ActivationValidationResult.Accepted();
    }

    private static bool ContainsWhitespaceOrControl(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]) || char.IsControl(value[i]))
            {
                return true;
            }
        }
        return false;
    }

    private static ActivationValidationResult MapDedupeOutcome(AcceptResult outcome) => outcome switch
    {
        AcceptResult.Accepted => ActivationValidationResult.Accepted(
            "activation newly stored in dedupe tracker"),
        AcceptResult.ReplayIdempotent => ActivationValidationResult.Reject(
            ActivationValidationReasons.ReplayIdempotent,
            "activation matches a previously stored entry with identical payload."),
        AcceptResult.RejectedDedupeConflict => ActivationValidationResult.Reject(
            ActivationValidationReasons.DedupeConflict,
            "activation matches a stored identity with mutated payload."),
        AcceptResult.RejectedAmbiguousDuplicate => ActivationValidationResult.Reject(
            ActivationValidationReasons.AmbiguousDuplicate,
            "concurrent candidates without a unique tiebreak rank."),
        AcceptResult.RejectedDedupeStoreInvalid => ActivationValidationResult.Reject(
            ActivationValidationReasons.DedupeStoreInvalid,
            "dedupe store has detected a load/state-validation failure."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome), outcome, $"Unknown AcceptResult: {outcome}"),
    };
}
