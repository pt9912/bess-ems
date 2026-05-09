using BatteryEms.Domain;

namespace BatteryEms.Api.Contracts;

// HTTP-side response shapes. Kept separate from Domain types so the wire
// contract can evolve without dragging Domain into JSON-serialisation
// concerns. snake_case JSON property names are applied centrally via the
// host's JsonSerializerOptions configuration.

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record HealthResponse(
    string Status,
    DateTimeOffset At,
    IReadOnlyDictionary<string, string>? Components = null);

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

// Body for POST /operator/stop. The operator identity is taken from the
// authenticated principal (LH-API-007) rather than the body so a caller
// cannot impersonate another operator just by editing the JSON.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OperatorStopRequestBody(string AssetId, string Reason);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OperatorStopResponse(
    string AssetId,
    string Operator,
    string Reason,
    DateTimeOffset ActivatedAt);

// Body for POST /markets/day-ahead/optimize. TimeStepSeconds keeps the
// wire shape JSON-friendly (TimeSpan would force ISO-8601 round-trip);
// the endpoint converts it to TimeSpan before handing off to the
// application. PricesPerStep + PriceUnit are optional but, when set,
// must align with the horizon (validated in the application layer).
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OptimizationRequestBody(
    string AssetId,
    string ScheduleType,
    DateTimeOffset HorizonStart,
    DateTimeOffset HorizonEnd,
    double TimeStepSeconds,
    IReadOnlyList<double>? PricesPerStep = null,
    string? PriceUnit = null);

// Body for POST /markets/intraday/reoptimize (RM-M4-01). ResidualStart
// is the moment from which the residual horizon is reoptimised; it
// must align to a window boundary of the existing Intraday schedule
// (D-02). ScheduleType is fixed to "intraday" for this endpoint —
// the use case enforces it; the request body is named distinctly to
// keep wire-shape stable as the day-ahead body evolves separately.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record IntradayReoptimizationRequestBody(
    string AssetId,
    DateTimeOffset ResidualStart,
    DateTimeOffset HorizonEnd,
    double TimeStepSeconds,
    IReadOnlyList<double>? PricesPerStep = null,
    string? PriceUnit = null);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OptimizationResponse(
    Guid RunId,
    OptimizationSolverStatus Status,
    DateTimeOffset HorizonStart,
    DateTimeOffset HorizonEnd,
    int? ProducedScheduleVersion,
    string TerminationReason);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OptimizationRunResponse(
    Guid RunId,
    string AssetId,
    string SolverName,
    OptimizationSolverStatus Status,
    DateTimeOffset HorizonStart,
    DateTimeOffset HorizonEnd,
    double TimeStepSeconds,
    double ObjectiveValue,
    IReadOnlyList<OptimizationObjectiveComponentView> ObjectiveBreakdown,
    IReadOnlyList<string> ConstraintViolations,
    IReadOnlyList<string> Warnings,
    double SolverRuntimeSeconds,
    string TerminationReason,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ScheduleReferenceView> Inputs,
    ScheduleReferenceView? ProducedSchedule)
{
    public static OptimizationRunResponse From(OptimizationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(
            RunId: run.RunId,
            AssetId: run.AssetId,
            SolverName: run.SolverName,
            Status: run.Status,
            HorizonStart: run.HorizonStart,
            HorizonEnd: run.HorizonEnd,
            TimeStepSeconds: run.TimeStep.TotalSeconds,
            ObjectiveValue: run.ObjectiveValue,
            ObjectiveBreakdown: run.ObjectiveBreakdown.Components
                .Select(c => new OptimizationObjectiveComponentView(c.Name, c.Value, c.Unit))
                .ToArray(),
            ConstraintViolations: run.ConstraintViolations,
            Warnings: run.Warnings,
            SolverRuntimeSeconds: run.SolverRuntime.TotalSeconds,
            TerminationReason: run.TerminationReason,
            CreatedAt: run.CreatedAt,
            Inputs: run.Inputs.Select(ScheduleReferenceView.From).ToArray(),
            ProducedSchedule: run.ProducedSchedule is null
                ? null
                : ScheduleReferenceView.From(run.ProducedSchedule));
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OptimizationObjectiveComponentView(
    string Name,
    double Value,
    string Unit);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ScheduleReferenceView(
    string AssetId,
    ScheduleType Type,
    int Version)
{
    public static ScheduleReferenceView From(ScheduleReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(reference.AssetId, reference.Type, reference.Version);
    }
}
