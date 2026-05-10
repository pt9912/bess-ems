namespace BatteryEms.Domain;

// Typed outcome of the Regelleistung activation validation pipeline
// (plan-RM-M4-03 §144). ReasonCode follows the project-wide kebab-case
// convention used by earlier slices (intraday-baseline-missing,
// concurrent-version-conflict, dedupe-store-invalid). Sub-Slice A
// introduces the type and the time-validation reasons; Sub-Slices B/C/D
// add dedupe, pipeline, and use-case reasons.
public sealed record ActivationValidationResult
{
    public required string ReasonCode { get; init; }
    public required string Details { get; init; }

    public bool IsAccepted => ReasonCode == ActivationValidationReasons.Accepted;

    public static ActivationValidationResult Accepted(string details = "") =>
        new() { ReasonCode = ActivationValidationReasons.Accepted, Details = details };

    public static ActivationValidationResult Reject(string reasonCode, string details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (reasonCode == ActivationValidationReasons.Accepted)
        {
            throw new ArgumentException(
                "Use ActivationValidationResult.Accepted() for the accepted outcome.",
                nameof(reasonCode));
        }
        return new ActivationValidationResult { ReasonCode = reasonCode, Details = details };
    }
}

public static class ActivationValidationReasons
{
    public const string Accepted = "accepted";

    // Schema check (Sub-Slice C): structural-format gate that runs
    // first in the pipeline so malformed identifiers / payload hashes
    // never reach the time validator or dedupe store.
    public const string SchemaInvalid = "schema-invalid";

    // Time-validator outcomes (Sub-Slice A).
    public const string TimestampStale = "timestamp-stale";
    public const string TimestampFutureSkew = "timestamp-future-skew";

    // Pipeline outcome surfaced when the timebase debounce state is
    // Degraded (Sub-Slice C orchestrator emits this; the constant is
    // pinned in A for cross-slice cohesion).
    public const string TimebaseDegraded = "timebase-degraded";

    // Dedupe outcomes (Sub-Slice C maps from AcceptResult).
    public const string ReplayIdempotent = "replay-idempotent";
    public const string DedupeConflict = "dedupe-conflict";
    public const string AmbiguousDuplicate = "ambiguous-duplicate";
    public const string DedupeStoreInvalid = "dedupe-store-invalid";

    // Additional codes (not-dispatch-relevant,
    // security-profile-enforcement-not-wired) land with Sub-Slice D.
}
