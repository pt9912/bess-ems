namespace BatteryEms.Domain;

// Deterministic PID kernel for LH-CTRL-004. Pure function: same inputs
// produce the same output, no clock/RNG dependency, framework-free.
//
// Anti-windup is conditional integration. The freeze guard checks the
// sign of the integrator update Ki·error, not the sign of the error
// alone — that way a configurator who legitimately uses negative Ki
// (sign-convention mismatch with the measurement frame) still gets a
// guard that freezes when the integrator is *worsening* saturation and
// allows it to relieve when it would unwind.
//
// Output clamping is symmetric on the post-integral sum and is
// mandatory: OutputMin/OutputMax are `required` on the options record,
// must be finite, and cannot be skipped by a forgetful caller.
//
// Deadband is an absolute threshold on the error. Inside the band the
// kernel zeroes the P term, suppresses the D term, and holds the
// integrator. PreviousError is preserved at the last real error so the
// derivative computed on the first out-of-band step measures the actual
// change since attention, not the spike that "deadband resets prev to
// zero" would produce.
//
// Derivative is computed on error (not on measurement). A step-change
// setpoint produces a derivative kick. Callers that want to suppress
// that kick warm-start PidControllerState.PreviousError with the
// initial error.
//
// Non-finite intermediates are an explicit error: the kernel validates
// that state and inputs are finite at entry, and after computing the
// pre-clamp output it rejects non-finite values with an OverflowException
// instead of letting +/-Infinity propagate through to the wire (the
// AdapterWriteLimiter's static clamp does not normalize NaN, so a
// non-finite output here would silently slip through).
//
// LH-CTRL-004 lists this as "Soll" — the kernel is delivered as a
// composable Domain primitive; no production wiring into the control
// loop is performed by this work package.
public static class PidController
{
    public static PidStepResult Step(
        PidControllerState state,
        PidControllerOptions options,
        double setpoint,
        double measurement,
        TimeSpan dt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();

        if (!double.IsFinite(state.Integral))
        {
            throw new ArgumentException(
                $"state.Integral must be finite (got {state.Integral}).",
                nameof(state));
        }
        if (!double.IsFinite(state.PreviousError))
        {
            throw new ArgumentException(
                $"state.PreviousError must be finite (got {state.PreviousError}).",
                nameof(state));
        }

        if (dt <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dt),
                $"Sample time must be positive (got {dt}).");
        }
        if (!double.IsFinite(setpoint))
        {
            throw new ArgumentException(
                $"Setpoint must be finite (got {setpoint}).",
                nameof(setpoint));
        }
        if (!double.IsFinite(measurement))
        {
            throw new ArgumentException(
                $"Measurement must be finite (got {measurement}).",
                nameof(measurement));
        }

        var dtSeconds = dt.TotalSeconds;
        var rawError = setpoint - measurement;

        var inDeadband = options.DeadbandAbsolute > 0
            && Math.Abs(rawError) < options.DeadbandAbsolute;
        var effectiveError = inDeadband ? 0.0 : rawError;

        var p = options.Kp * effectiveError;
        var d = inDeadband
            ? 0.0
            : options.Kd * (effectiveError - state.PreviousError) / dtSeconds;
        var integratorStep = options.Ki * effectiveError;
        var candidateIntegral = state.Integral + (integratorStep * dtSeconds);

        var candidatePreClamp = p + candidateIntegral + d;

        var integralFrozen = false;
        var chosenIntegral = candidateIntegral;
        if (options.AntiWindupMode == PidAntiWindupMode.ConditionalIntegration)
        {
            // Freeze when the integrator update would push *further*
            // past the saturation bound. Direction is decided by the
            // sign of integratorStep (= Ki·error), not error alone.
            if (candidatePreClamp > options.OutputMax && integratorStep > 0)
            {
                chosenIntegral = state.Integral;
                integralFrozen = true;
            }
            else if (candidatePreClamp < options.OutputMin && integratorStep < 0)
            {
                chosenIntegral = state.Integral;
                integralFrozen = true;
            }
        }

        var preClamp = p + chosenIntegral + d;
        if (!double.IsFinite(preClamp))
        {
            throw new OverflowException(
                $"PID step produced a non-finite output (P={p}, I={chosenIntegral}, D={d}). "
                + "Reduce gains or widen the sample time.");
        }

        var clamped = Math.Clamp(preClamp, options.OutputMin, options.OutputMax);
        var wasClamped = clamped != preClamp;

        var nextState = new PidControllerState
        {
            Integral = chosenIntegral,
            // Preserve the last real error across the deadband so the
            // derivative on exit measures the actual change since the
            // controller last paid attention, not a spike against zero.
            PreviousError = inDeadband ? state.PreviousError : effectiveError,
        };

        return new PidStepResult(nextState, clamped, wasClamped, integralFrozen);
    }
}
