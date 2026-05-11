using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

// Plan-RM-M5 §Fallback-Plan-Gueltigkeit Pins. Vier Achsen: Kontext-
// Stempel / Zeitindex / MaxAge / Telemetrie-Drift. Reihenfolge des
// Short-Circuits ist gepinnt (Kontext ZUERST).
public sealed class DefaultFallbackPlanValidatorTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TimeStep = TimeSpan.FromHours(1);

    private static BatteryAsset Asset() => new(
        assetId: "asset-1",
        capacityKwh: 100, maxChargePowerKw: 50, maxDischargePowerKw: 50,
        minSocPercent: 10, maxSocPercent: 90,
        chargeEfficiency: 0.95, dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 10,
        minOperatingTemperatureCelsius: -20, maxOperatingTemperatureCelsius: 55);

    private static Schedule SampleSchedule(
        string assetId = "asset-1",
        ScheduleType type = ScheduleType.DayAhead,
        string marketBidArea = "DE-LU",
        int windows = 4)
    {
        var ws = new ScheduleWindow[windows];
        for (var i = 0; i < windows; i++)
        {
            ws[i] = new ScheduleWindow(
                Start: T0 + TimeSpan.FromHours(i),
                End: T0 + TimeSpan.FromHours(i + 1),
                TargetPowerKw: 0.0);
        }
        return new Schedule(assetId, type, marketBidArea, version: 1, windows: ws);
    }

    private static BatteryTelemetry SampleTelemetry(
        double socPercent = 50,
        double temperature = 25) =>
        new(
            Timestamp: T0,
            AssetId: "asset-1",
            SocPercent: socPercent,
            SohPercent: 100,
            ActivePowerKw: 0,
            ReactivePowerKvar: 0,
            DcVoltage: 800,
            DcCurrent: 0,
            TemperatureCelsius: temperature,
            Available: true,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid);

    private static FallbackPlanContext SampleContext(
        DateTimeOffset? currentTickUtc = null,
        string assetId = "asset-1",
        ScheduleType type = ScheduleType.DayAhead,
        string marketBidArea = "DE-LU",
        BatteryTelemetry? telemetry = null) =>
        new(
            AssetId: assetId,
            ScheduleType: type,
            CurrentTickUtc: currentTickUtc ?? T0.AddMinutes(30),
            HorizonStart: T0,
            HorizonEnd: T0.AddHours(4),
            TimeStep: TimeStep,
            MarketBidArea: marketBidArea,
            Asset: Asset(),
            CurrentTelemetry: telemetry ?? SampleTelemetry());

    // Default ControlCycleInterval=30min damit MaxAge = min(TimeStep=1h,
    // 2*30min) = 1h reicht für die generischen Pin-Szenarien; spezifische
    // Pins überschreiben das explizit.
    private static DefaultFallbackPlanValidator Build(
        TimeSpan? controlCycleInterval = null) =>
        new(new FallbackPlanValidatorOptions
        {
            ControlCycleInterval = controlCycleInterval ?? TimeSpan.FromMinutes(30),
        });

    [Fact]
    public void Valid_candidate_passes_all_four_axes()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(SampleSchedule(), T0.AddMinutes(-1));

        var result = validator.Validate(candidate, SampleContext());

        Assert.True(result.IsValid);
        Assert.Equal(FallbackReason.None, result.Reason);
    }

    [Fact]
    public void Mismatched_asset_id_fails_with_context_mismatch()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(
            SampleSchedule(assetId: "asset-other"), T0);

        var result = validator.Validate(candidate, SampleContext(assetId: "asset-1"));

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackContextMismatch, result.Reason);
        Assert.Contains("asset-id-mismatch", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Mismatched_schedule_type_fails_with_context_mismatch()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(
            SampleSchedule(type: ScheduleType.Intraday), T0);

        var result = validator.Validate(
            candidate, SampleContext(type: ScheduleType.DayAhead));

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackContextMismatch, result.Reason);
        Assert.Contains("schedule-type-mismatch", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Mismatched_market_bid_area_fails_with_context_mismatch()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(
            SampleSchedule(marketBidArea: "DE-AT-LU"), T0);

        var result = validator.Validate(
            candidate, SampleContext(marketBidArea: "DE-LU"));

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackContextMismatch, result.Reason);
    }

    [Fact]
    public void Current_tick_before_horizon_fails_with_plan_expired()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(SampleSchedule(), T0);
        var context = SampleContext(currentTickUtc: T0.AddHours(-1));

        var result = validator.Validate(candidate, context);

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackPlanExpired, result.Reason);
        Assert.Contains("current-tick-outside-horizon", result.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Current_tick_after_horizon_fails_with_plan_expired()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(SampleSchedule(), T0);
        var context = SampleContext(currentTickUtc: T0.AddHours(5));

        var result = validator.Validate(candidate, context);

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackPlanExpired, result.Reason);
    }

    [Fact]
    public void Plan_older_than_max_age_fails_with_plan_expired()
    {
        // ControlCycleInterval=1s ⇒ derived MaxAge = min(TimeStep,
        // 2 * 1s) = 2s. TimeStep im SampleContext ist 1h, also gilt
        // 2s als MaxAge. Plan-CreatedAt liegt 10s vor Current-Tick →
        // expired.
        var validator = Build(controlCycleInterval: TimeSpan.FromSeconds(1));
        var candidate = new FallbackPlanCandidate(
            SampleSchedule(), T0.AddMinutes(30).AddSeconds(-10));
        var context = SampleContext(currentTickUtc: T0.AddMinutes(30));

        var result = validator.Validate(candidate, context);

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackPlanExpired, result.Reason);
        Assert.Contains("plan-age-exceeded", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_within_max_age_passes()
    {
        // ControlCycleInterval=10min ⇒ derived MaxAge = 20min < 1h.
        // Plan 5min alt → within.
        var validator = Build(controlCycleInterval: TimeSpan.FromMinutes(10));
        var candidate = new FallbackPlanCandidate(
            SampleSchedule(), T0.AddMinutes(30).AddMinutes(-5));
        var context = SampleContext(currentTickUtc: T0.AddMinutes(30));

        var result = validator.Validate(candidate, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Telemetry_soc_outside_bounds_fails_with_drift()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(SampleSchedule(), T0);
        var context = SampleContext(
            telemetry: SampleTelemetry(socPercent: 5)); // below 10% MinSoc

        var result = validator.Validate(candidate, context);

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackTelemetryDrift, result.Reason);
        Assert.Contains("soc-out-of-bounds", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Telemetry_temperature_outside_bounds_fails_with_drift()
    {
        var validator = Build();
        var candidate = new FallbackPlanCandidate(SampleSchedule(), T0);
        var context = SampleContext(
            telemetry: SampleTelemetry(temperature: 70)); // above 55°C max

        var result = validator.Validate(candidate, context);

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackTelemetryDrift, result.Reason);
    }

    [Fact]
    public void Missing_telemetry_does_not_fail_drift_check()
    {
        // CurrentTelemetry=null → keine Drift-Detection möglich;
        // Validator gibt durch. Worker entscheidet auf der oberen
        // Schicht ob er ohne Snapshot fortfährt.
        var validator = Build();
        var candidate = new FallbackPlanCandidate(SampleSchedule(), T0);
        var context = SampleContext() with { CurrentTelemetry = null };

        var result = validator.Validate(candidate, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Context_check_runs_before_age_check()
    {
        // Ein ungültiger Kontext + zugleich ein abgelaufener Plan:
        // Validator meldet Kontext-Mismatch (short-circuit-Reihenfolge).
        var validator = Build();
        var candidate = new FallbackPlanCandidate(
            SampleSchedule(assetId: "wrong"), T0.AddYears(-1));

        var result = validator.Validate(candidate, SampleContext());

        Assert.Equal(FallbackReason.FallbackContextMismatch, result.Reason);
    }

    [Fact]
    public void Empty_schedule_windows_falls_back_to_plan_expired()
    {
        // Schedule-Ctor verbietet 0-Windows, also können wir das nicht
        // direkt konstruieren. Wir nutzen einen Single-Window-Schedule
        // mit Current-Tick außerhalb statt — semantically derselbe Pfad.
        var validator = Build();
        var schedule = new Schedule(
            assetId: "asset-1",
            type: ScheduleType.DayAhead,
            marketBidArea: "DE-LU",
            version: 1,
            windows: new[]
            {
                new ScheduleWindow(T0, T0.AddHours(1), 0.0),
            });
        var candidate = new FallbackPlanCandidate(schedule, T0);
        var context = SampleContext(currentTickUtc: T0.AddHours(2));

        var result = validator.Validate(candidate, context);

        Assert.False(result.IsValid);
        Assert.Equal(FallbackReason.FallbackPlanExpired, result.Reason);
    }

    [Fact]
    public void Operator_override_per_schedule_type_takes_precedence()
    {
        // Default-Formel würde MaxAge=2s liefern; Operator-Override
        // pro Intraday-ScheduleType verschiebt die Schwelle auf 1h.
        var validator = new DefaultFallbackPlanValidator(
            new FallbackPlanValidatorOptions
            {
                ControlCycleInterval = TimeSpan.FromSeconds(1),
                OverridesPerType = new Dictionary<ScheduleType, TimeSpan>
                {
                    [ScheduleType.DayAhead] = TimeSpan.FromHours(1),
                },
            });
        var candidate = new FallbackPlanCandidate(
            SampleSchedule(), T0.AddMinutes(30).AddMinutes(-30));
        var context = SampleContext(currentTickUtc: T0.AddMinutes(30));

        var result = validator.Validate(candidate, context);

        Assert.True(result.IsValid);
    }
}
