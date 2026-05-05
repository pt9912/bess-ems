namespace BatteryEms.Application.IO;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record CommandDispatchResult(
    bool Success,
    string Reason,
    DateTimeOffset DispatchedAt)
{
    public static CommandDispatchResult Ok(DateTimeOffset at, string reason = "ok") =>
        new(true, reason, at);

    public static CommandDispatchResult Failed(string reason, DateTimeOffset at) =>
        new(false, reason, at);
}
