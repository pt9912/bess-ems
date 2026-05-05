namespace BatteryEms.Application.Optimization;

public sealed record DispatchResult(
    string RequestId,
    double TargetActivePowerKw,
    string Reason,
    bool IsValid)
{
    public static DispatchResult Idle(string requestId, string reason) =>
        new(requestId, 0, reason, true);

    public static DispatchResult Invalid(string requestId, string reason) =>
        new(requestId, 0, reason, false);
}
