namespace BatteryEms.Domain;

// Deterministic PID kernel for LH-CTRL-004. Pure function: same inputs
// produce the same output, no clock/RNG dependency, framework-free.
// Anti-windup is conditional integration (freeze the integral when the
// pre-clamp output saturates and the current error would push it
// further past the bound). Output clamping is symmetric on the
// post-integral sum. Deadband is an absolute threshold on the error;
// inside it, P/I/D contributions all see error = 0, so the integrator
// holds the output at the previously accumulated position.
//
// Derivative is computed on error (not on measurement), which keeps
// the kernel symmetric in setpoint and measurement but means a
// step-change setpoint produces a derivative kick. Callers that want
// to suppress that kick warm-start PidControllerState.PreviousError
// with the initial error.
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
        var d = options.Kd * (effectiveError - state.PreviousError) / dtSeconds;
        var candidateIntegral = state.Integral + (options.Ki * effectiveError * dtSeconds);

        var candidatePreClamp = p + candidateIntegral + d;

        var integralFrozen = false;
        var chosenIntegral = candidateIntegral;
        if (options.AntiWindupMode == PidAntiWindupMode.ConditionalIntegration)
        {
            if (candidatePreClamp > options.OutputMax && effectiveError > 0)
            {
                chosenIntegral = state.Integral;
                integralFrozen = true;
            }
            else if (candidatePreClamp < options.OutputMin && effectiveError < 0)
            {
                chosenIntegral = state.Integral;
                integralFrozen = true;
            }
        }

        var preClamp = p + chosenIntegral + d;
        var clamped = Math.Clamp(preClamp, options.OutputMin, options.OutputMax);
        var wasClamped = clamped != preClamp;

        var nextState = new PidControllerState
        {
            Integral = chosenIntegral,
            PreviousError = effectiveError,
            LastOutput = clamped,
        };

        return new PidStepResult(nextState, clamped, wasClamped, integralFrozen);
    }
}
