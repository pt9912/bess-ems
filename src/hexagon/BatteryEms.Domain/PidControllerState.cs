namespace BatteryEms.Domain;

// Functional PID state: an immutable snapshot threaded through Step
// calls. Inside the deadband PreviousError is held at the prior real
// error (not overwritten to 0) so the derivative term across a deadband
// transition computes the actual error change instead of producing a
// kick on exit.
public sealed record PidControllerState
{
    public double Integral { get; init; }
    public double PreviousError { get; init; }

    public static PidControllerState Initial { get; } = new();
}
