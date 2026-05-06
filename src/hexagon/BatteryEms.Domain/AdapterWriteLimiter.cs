namespace BatteryEms.Domain;

// Final asset-static clamp applied by every protocol adapter immediately
// before sending a command on the wire (LH-SAFE-007, RM-M1-11). This is
// defense-in-depth: ConstraintLimiter and RampLimiter run in the control
// loop with live telemetry, but a misconfiguration, application bug, or
// out-of-band command path could still produce a BatteryCommand whose
// active power exceeds the asset's static rating. The adapter applies
// this last-line clamp without touching telemetry so the safety net is
// independent of the realtime snapshot pipeline.
//
// Scope is intentionally narrow:
//   - Mode in {Stop, Idle} forces ActivePowerKw to 0 (a non-zero
//     setpoint contradicts the operating mode and would yield ambiguous
//     behaviour at the field device).
//   - ActivePowerKw is clamped to [-MaxChargePowerKw, +MaxDischargePowerKw].
//
// SOC / temperature / ramp limits stay with the application layer
// (ConstraintLimiter, RampLimiter) — they need live telemetry, which the
// adapter does not own.
public static class AdapterWriteLimiter
{
    public static AdapterWriteLimitResult Apply(BatteryCommand command, BatteryAsset asset)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(asset);

        if ((command.Mode == CommandMode.Stop || command.Mode == CommandMode.Idle)
            && command.ActivePowerKw != 0)
        {
            var reason = command.Mode == CommandMode.Stop
                ? "mode-stop-zero-power"
                : "mode-idle-zero-power";
            return AdapterWriteLimitResult.Limited(
                command with { ActivePowerKw = 0 },
                reason);
        }

        if (command.ActivePowerKw > asset.MaxDischargePowerKw)
        {
            return AdapterWriteLimitResult.Limited(
                command with { ActivePowerKw = asset.MaxDischargePowerKw },
                "max-discharge-power");
        }

        if (command.ActivePowerKw < -asset.MaxChargePowerKw)
        {
            return AdapterWriteLimitResult.Limited(
                command with { ActivePowerKw = -asset.MaxChargePowerKw },
                "max-charge-power");
        }

        return AdapterWriteLimitResult.Unchanged(command);
    }
}
