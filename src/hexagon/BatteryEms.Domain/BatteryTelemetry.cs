namespace BatteryEms.Domain;

public enum DataQualityState
{
    Valid,
    Stale,
    Substituted,
    ProtocolError,
}

public sealed record DataQuality(DataQualityState Flag, string Reason)
{
    public static DataQuality Valid { get; } = new(DataQualityState.Valid, "valid");
    public static DataQuality Stale(string reason) => new(DataQualityState.Stale, reason);
    public static DataQuality Substituted(string reason) => new(DataQualityState.Substituted, reason);
    public static DataQuality ProtocolError(string reason) => new(DataQualityState.ProtocolError, reason);

    public bool IsUsableForControl => Flag == DataQualityState.Valid;
}

public sealed record BatteryTelemetry(
    DateTimeOffset Timestamp,
    string AssetId,
    double SocPercent,
    double SohPercent,
    double ActivePowerKw,
    double ReactivePowerKvar,
    double DcVoltage,
    double DcCurrent,
    double TemperatureCelsius,
    bool Available,
    string FaultStatus,
    DataQuality DataQuality);
