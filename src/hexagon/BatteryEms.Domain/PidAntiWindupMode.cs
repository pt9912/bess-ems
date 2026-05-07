namespace BatteryEms.Domain;

public enum PidAntiWindupMode
{
    // Freeze the integrator when the candidate output is past the
    // saturation bound and the integrator step (Ki·error) has the same
    // sign as the violation — i.e. continuing to integrate would not
    // relieve it. Direction is the integrator-step sign, not the error
    // sign, so a negative-Ki configuration with positive error
    // correctly does NOT freeze when the integrator is decrementing
    // toward relief.
    ConditionalIntegration = 0,
}
