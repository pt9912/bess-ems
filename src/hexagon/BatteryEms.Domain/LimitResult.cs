namespace BatteryEms.Domain;

public sealed record LimitResult(double LimitedActivePowerKw, bool WasLimited, string LimitReason)
{
    public static LimitResult Unchanged(double powerKw) =>
        new(powerKw, false, "within-limits");

    public static LimitResult Clamped(double clampedPowerKw, string reason) =>
        new(clampedPowerKw, true, reason);
}
