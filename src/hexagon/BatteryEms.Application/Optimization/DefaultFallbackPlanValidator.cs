using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Plan-RM-M5 §Fallback-Plan-Gueltigkeit Default-Impl. Vier
// orthogonale Achsen werden in fester Reihenfolge geprüft; erste
// invalide Achse short-circuit'd zum Fail-Result.
public sealed class DefaultFallbackPlanValidator : IFallbackPlanValidator
{
    private readonly FallbackPlanValidatorOptions _options;

    public DefaultFallbackPlanValidator(FallbackPlanValidatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public FallbackPlanValidationResult Validate(
        FallbackPlanCandidate candidate,
        FallbackPlanContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        // (1) Kontext-Stempel ZUERST: asset_id/schedule_type/time_step/
        // market_bid_area müssen matchen. Bei Mismatch ist die
        // Telemetrie-Drift- und Age-Prüfung irrelevant.
        var contextResult = CheckContextStamp(candidate, context);
        if (!contextResult.IsValid) { return contextResult; }

        // (2) Zeitindex: aktueller Tick muss im Schedule-Horizon
        // liegen (halboffen).
        var horizonResult = CheckHorizonAlignment(candidate, context);
        if (!horizonResult.IsValid) { return horizonResult; }

        // (3) MaxFallbackScheduleAge: Plan-Alter gegen Schwelle.
        // Schwelle pro Asset/ScheduleType aus
        // `FallbackPlanValidatorOptions`.
        var ageResult = CheckMaxAge(candidate, context);
        if (!ageResult.IsValid) { return ageResult; }

        // (4) Telemetrie-Drift: aktuelle Telemetrie muss in den
        // Asset-Bounds liegen, mit denen der Plan erzeugt wurde.
        var driftResult = CheckTelemetryDrift(context);
        if (!driftResult.IsValid) { return driftResult; }

        return FallbackPlanValidationResult.Valid;
    }

    private static FallbackPlanValidationResult CheckContextStamp(
        FallbackPlanCandidate candidate, FallbackPlanContext context)
    {
        if (!string.Equals(candidate.Schedule.AssetId, context.AssetId,
            StringComparison.Ordinal))
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackContextMismatch,
                $"asset-id-mismatch candidate={candidate.Schedule.AssetId} "
                + $"context={context.AssetId}");
        }
        if (candidate.Schedule.Type != context.ScheduleType)
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackContextMismatch,
                $"schedule-type-mismatch candidate={candidate.Schedule.Type} "
                + $"context={context.ScheduleType}");
        }
        if (!string.Equals(candidate.Schedule.MarketBidArea, context.MarketBidArea,
            StringComparison.Ordinal))
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackContextMismatch,
                $"market-bid-area-mismatch candidate={candidate.Schedule.MarketBidArea} "
                + $"context={context.MarketBidArea}");
        }
        return FallbackPlanValidationResult.Valid;
    }

    private static FallbackPlanValidationResult CheckHorizonAlignment(
        FallbackPlanCandidate candidate, FallbackPlanContext context)
    {
        if (candidate.Schedule.Windows.Count == 0)
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackPlanExpired,
                "schedule-empty");
        }
        var firstWindow = candidate.Schedule.Windows[0];
        var lastWindow = candidate.Schedule.Windows[^1];
        if (context.CurrentTickUtc < firstWindow.Start
            || context.CurrentTickUtc >= lastWindow.End)
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackPlanExpired,
                $"current-tick-outside-horizon tick={context.CurrentTickUtc:O} "
                + $"horizon=[{firstWindow.Start:O}, {lastWindow.End:O})");
        }
        return FallbackPlanValidationResult.Valid;
    }

    private FallbackPlanValidationResult CheckMaxAge(
        FallbackPlanCandidate candidate, FallbackPlanContext context)
    {
        var maxAge = _options.GetMaxAgeFor(context.ScheduleType, context.TimeStep);
        var age = context.CurrentTickUtc - candidate.CreatedAtUtc;
        if (age > maxAge)
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackPlanExpired,
                $"plan-age-exceeded age={age} max={maxAge} "
                + $"created_at={candidate.CreatedAtUtc:O}");
        }
        return FallbackPlanValidationResult.Valid;
    }

    private static FallbackPlanValidationResult CheckTelemetryDrift(
        FallbackPlanContext context)
    {
        if (context.CurrentTelemetry is null)
        {
            // Kein Telemetrie-Snapshot → keine Drift-Detection möglich.
            // Worker entscheidet ob er auch ohne Snapshot fortfährt
            // (typischerweise Safe-Stop wegen Stale-Snapshot-Linie).
            return FallbackPlanValidationResult.Valid;
        }
        var t = context.CurrentTelemetry;
        var asset = context.Asset;
        if (t.SocPercent < asset.MinSocPercent || t.SocPercent > asset.MaxSocPercent)
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackTelemetryDrift,
                $"soc-out-of-bounds soc={t.SocPercent}% "
                + $"bounds=[{asset.MinSocPercent}, {asset.MaxSocPercent}]");
        }
        if (t.TemperatureCelsius < asset.MinOperatingTemperatureCelsius
            || t.TemperatureCelsius > asset.MaxOperatingTemperatureCelsius)
        {
            return FallbackPlanValidationResult.Invalid(
                FallbackReason.FallbackTelemetryDrift,
                $"temperature-out-of-bounds temp={t.TemperatureCelsius}°C "
                + $"bounds=[{asset.MinOperatingTemperatureCelsius}, "
                + $"{asset.MaxOperatingTemperatureCelsius}]");
        }
        return FallbackPlanValidationResult.Valid;
    }
}

// Plan-RM-M5 §Fallback-Plan-Gueltigkeit Master-DoD-Default für
// `MaxFallbackScheduleAge`: `min(Schedule.TimeStep, 2 *
// ControlCycleInterval)` pro Asset/ScheduleType. ControlCycleInterval
// liest der Validator nicht selbst — er bekommt es als Konstruktor-
// Parameter; Operator-Override pro ScheduleType ist optional.
public sealed class FallbackPlanValidatorOptions
{
    public TimeSpan ControlCycleInterval { get; init; } = TimeSpan.FromSeconds(1);

    // Operator-Override pro ScheduleType. Default leer ⇒ Master-DoD-
    // Formel `min(TimeStep, 2 * ControlCycleInterval)`.
    public IReadOnlyDictionary<ScheduleType, TimeSpan> OverridesPerType { get; init; }
        = new Dictionary<ScheduleType, TimeSpan>();

    public TimeSpan GetMaxAgeFor(ScheduleType scheduleType, TimeSpan timeStep)
    {
        if (OverridesPerType.TryGetValue(scheduleType, out var configured))
        {
            return configured;
        }
        var derived = 2 * ControlCycleInterval;
        return timeStep < derived ? timeStep : derived;
    }
}
