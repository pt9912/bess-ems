using BatteryEms.Domain;

namespace BatteryEms.Api.Contracts;

// HTTP-side response shapes. Kept separate from Domain types so the wire
// contract can evolve without dragging Domain into JSON-serialisation
// concerns. snake_case JSON property names are applied centrally via the
// host's JsonSerializerOptions configuration.

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record HealthResponse(string Status, DateTimeOffset At);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record BatteryStatusResponse(
    string AssetId,
    TelemetryView? Telemetry,
    DataQualityView? Quality,
    DateTimeOffset? ObservedAt,
    CommandView? LastCommand);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record CommandResponse(string AssetId, CommandView? Command);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record SchedulesResponse(string AssetId, IReadOnlyList<ScheduleView> Schedules);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record TelemetryView(
    DateTimeOffset Timestamp,
    double SocPercent,
    double SohPercent,
    double ActivePowerKw,
    double ReactivePowerKvar,
    double DcVoltage,
    double DcCurrent,
    double TemperatureCelsius,
    bool Available,
    string FaultStatus)
{
    public static TelemetryView From(BatteryTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        return new(
            Timestamp: telemetry.Timestamp,
            SocPercent: telemetry.SocPercent,
            SohPercent: telemetry.SohPercent,
            ActivePowerKw: telemetry.ActivePowerKw,
            ReactivePowerKvar: telemetry.ReactivePowerKvar,
            DcVoltage: telemetry.DcVoltage,
            DcCurrent: telemetry.DcCurrent,
            TemperatureCelsius: telemetry.TemperatureCelsius,
            Available: telemetry.Available,
            FaultStatus: telemetry.FaultStatus);
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record DataQualityView(string Flag, string Reason)
{
    public static DataQualityView From(DataQuality quality)
    {
        ArgumentNullException.ThrowIfNull(quality);
        return new(quality.Flag.ToString(), quality.Reason);
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record CommandView(
    string CommandId,
    DateTimeOffset Timestamp,
    string AssetId,
    string Mode,
    double ActivePowerKw,
    double? ReactivePowerKvar,
    DateTimeOffset ValidUntil,
    string Reason,
    string Source)
{
    public static CommandView From(BatteryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(
            CommandId: command.CommandId,
            Timestamp: command.Timestamp,
            AssetId: command.AssetId,
            Mode: command.Mode.ToString(),
            ActivePowerKw: command.ActivePowerKw,
            ReactivePowerKvar: command.ReactivePowerKvar,
            ValidUntil: command.ValidUntil,
            Reason: command.Reason,
            Source: command.Source.ToString());
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ScheduleView(
    string Type,
    string MarketBidArea,
    int Version,
    DateTimeOffset HorizonStart,
    DateTimeOffset HorizonEnd,
    IReadOnlyList<ScheduleWindowView> Windows)
{
    public static ScheduleView From(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return new(
            Type: schedule.Type.ToString(),
            MarketBidArea: schedule.MarketBidArea,
            Version: schedule.Version,
            HorizonStart: schedule.HorizonStart,
            HorizonEnd: schedule.HorizonEnd,
            Windows: schedule.Windows
                .Select(w => new ScheduleWindowView(w.Start, w.End, w.TargetPowerKw))
                .ToArray());
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ScheduleWindowView(
    DateTimeOffset Start,
    DateTimeOffset End,
    double TargetPowerKw);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OperatorStopRequestBody(string AssetId, string Operator, string Reason);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OperatorStopResponse(
    string AssetId,
    string Operator,
    string Reason,
    DateTimeOffset ActivatedAt);
