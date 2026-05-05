namespace BatteryEms.Domain;

public enum CommandMode
{
    Stop,
    Charge,
    Discharge,
    Idle,
}

public enum CommandSource
{
    Schedule,
    Operator,
    RegelLeistung,
    Safety,
    Optimization,
    Fallback,
}

public sealed record BatteryCommand(
    string CommandId,
    DateTimeOffset Timestamp,
    string AssetId,
    CommandMode Mode,
    double ActivePowerKw,
    double? ReactivePowerKvar,
    DateTimeOffset ValidUntil,
    string Reason,
    CommandSource Source)
{
    public static BatteryCommand SafeStop(string assetId, DateTimeOffset now, TimeSpan validity, string reason, CommandSource source) =>
        new(
            CommandId: $"safe-stop-{now.ToUnixTimeMilliseconds()}-{assetId}",
            Timestamp: now,
            AssetId: assetId,
            Mode: CommandMode.Stop,
            ActivePowerKw: 0,
            ReactivePowerKvar: 0,
            ValidUntil: now + validity,
            Reason: reason,
            Source: source);

    public bool IsExpired(DateTimeOffset now) => now > ValidUntil;
}
