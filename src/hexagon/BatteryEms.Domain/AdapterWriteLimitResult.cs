namespace BatteryEms.Domain;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record AdapterWriteLimitResult(BatteryCommand Command, bool WasLimited, string Reason)
{
    public static AdapterWriteLimitResult Unchanged(BatteryCommand command) =>
        new(command, false, "ok");

    public static AdapterWriteLimitResult Limited(BatteryCommand command, string reason) =>
        new(command, true, reason);
}
