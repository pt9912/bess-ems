using BatteryEms.Domain;

namespace BatteryEms.Application.Control;

// RM-M3-05 managed-side implementation of the control kernel.
// Reproduces the inline Constraint+Ramp pipeline that
// ControlCycleUseCase carried in M1/M2 — same call order, same
// reason precedence (constraint reason wins when both limit) so
// every existing test fixture lands on the same KernelResult the
// pre-RM-M3-05 cycle produced.
public sealed class ManagedControlKernel : IControlKernel
{
    public KernelResult Compute(KernelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var constrained = ConstraintLimiter.Apply(
            input.Asset, input.Telemetry, input.DispatchTargetActivePowerKw);

        LimitResult ramped;
        if (input.PreviousActivePowerKw is double previous)
        {
            ramped = RampLimiter.Apply(
                input.Asset, previous,
                constrained.LimitedActivePowerKw, input.TimeSinceLastCommand);
        }
        else
        {
            ramped = LimitResult.Unchanged(constrained.LimitedActivePowerKw);
        }

        // Constraint reason wins when both clamped — matches the
        // pre-port behaviour pinned by the existing cycle tests.
        var reason = constrained.WasLimited
            ? constrained.LimitReason
            : ramped.WasLimited
                ? ramped.LimitReason
                : "within-limits";

        return new KernelResult(
            ActivePowerKw: ramped.LimitedActivePowerKw,
            Reason: reason,
            WasLimited: constrained.WasLimited || ramped.WasLimited,
            Source: KernelResultSource.Managed);
    }
}
