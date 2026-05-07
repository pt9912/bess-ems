namespace BatteryEms.Domain;

// Functional PID state: an immutable snapshot threaded through Step
// calls. PreviousError stores the error after deadband suppression so
// the derivative term stays consistent when the controller enters or
// leaves the deadband. LastOutput records the post-clamp output and is
// kept for callers that want to inspect saturation history; the Step
// kernel itself does not consume it.
public sealed record PidControllerState
{
    public double Integral { get; init; }
    public double PreviousError { get; init; }
    public double LastOutput { get; init; }

    public static PidControllerState Initial { get; } = new();
}
