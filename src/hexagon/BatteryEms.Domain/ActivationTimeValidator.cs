namespace BatteryEms.Domain;

// Per-sample stateless time validation for incoming Regelleistung
// activations (plan-RM-M4-03 §144). The caller supplies the current UTC
// instant — we deliberately do NOT take an IClock here so the kernel
// stays Domain-pure (analog to PidController.Step taking dt instead of
// owning a clock). Application wires IClock.UtcNow at the boundary.
//
// Two checks:
//   - Stale: now - signalTimestamp > MaxAge -> timestamp-stale.
//   - Future skew: signalTimestamp - now > FutureSkewTolerance ->
//     timestamp-future-skew. A single sample whose timestamp is far in
//     the future is already a clock-rollback suspect and is rejected
//     fail-closed (per-sample interpretation per plan §7; cross-sample
//     monotonic-state is out of scope).
//
// Validity-window (ValidFrom/ValidUntil) is NOT checked here — the
// optimizer evaluates that per-tick (Sub-Slice D), which lets a long
// validity window be re-checked against later wall-clock without
// re-running this validator.
public static class ActivationTimeValidator
{
    public static ActivationValidationResult Validate(
        RegelleistungActivation activation,
        DateTimeOffset now,
        RegelleistungOptions options)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();

        var age = now - activation.SignalTimestampUtc;
        if (age > options.MaxAge)
        {
            return ActivationValidationResult.Reject(
                ActivationValidationReasons.TimestampStale,
                $"signal age {age.TotalMilliseconds:F0}ms exceeds MaxAge {options.MaxAge.TotalMilliseconds:F0}ms");
        }
        if (-age > options.FutureSkewTolerance)
        {
            return ActivationValidationResult.Reject(
                ActivationValidationReasons.TimestampFutureSkew,
                $"signal future-skew {(-age).TotalMilliseconds:F0}ms exceeds tolerance {options.FutureSkewTolerance.TotalMilliseconds:F0}ms");
        }
        return ActivationValidationResult.Accepted();
    }
}
