namespace BatteryEms.Domain;

public enum PidAntiWindupMode
{
    // Freeze the integral when the candidate output saturates and the
    // current error would push it further past the saturation bound.
    ConditionalIntegration = 0,
}
