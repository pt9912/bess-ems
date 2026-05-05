namespace BatteryEms.Application.IO;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record AdapterStatus(
    bool Connected,
    DateTimeOffset? LastSuccessfulReadAt,
    string? LastError,
    long ConsecutiveFailures)
{
    public static AdapterStatus Disconnected { get; } = new(false, null, null, 0);
}
