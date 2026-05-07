using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Google.OrTools.LinearSolver;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

public sealed class OrToolsScheduleOptimizerModelTests
{
    private const double Tolerance = 1e-3;

    private static readonly double[] FlatPrices = { 50.0, 50.0, 50.0, 50.0 };
    private static readonly double[] FlatZero = { 0.0, 0.0 };
    private static readonly double[] CheapThenExpensive = { 10.0, 200.0 };
    private static readonly double[] HugeSpread = { 1.0, 1000.0 };
    private static readonly double[] LowHighPair = { 1.0, 200.0 };
    private static readonly double[] HalfHourPattern = { 10.0, 10.0, 200.0, 200.0 };
    private static readonly double[] FlatHundred = { 100.0, 100.0 };
    private static readonly double[] LowHighShort = { 10.0, 100.0 };
    private static readonly double[] ThreeStepProfile = { 10.0, 100.0, 200.0 };

    [Fact]
    public async Task Constant_prices_with_free_terminal_soc_drain_battery_for_end_of_horizon_revenue()
    {
        // M2-minimal design: terminal SOC is unconstrained (plan §Offene
        // Designentscheidungen), so the LP empties the battery at any
        // positive price — there is no penalty for ending below initial
        // SOC. Once OPEN-04 lands a cycle-balanced terminal SOC, this
        // expectation flips back to "idle schedule".
        var optimizer = Build();
        var request = NewRequest(FlatPrices, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal(4, result.ProducedSchedule!.Windows.Count);

        // Tightened (review #6): no charging at constant price (round-trip
        // η<1 makes any charge strictly loss-making), and the energy
        // delivered to the grid equals exactly the band drained from
        // initial SOC times the discharge efficiency.
        var asset = request.Asset;
        var initialSocKwh = (asset.MinSocPercent + asset.MaxSocPercent) / 200.0 * asset.CapacityKwh;
        var minSocKwh = asset.MinSocPercent / 100.0 * asset.CapacityKwh;
        var availableEnergyKwh = initialSocKwh - minSocKwh;
        var expectedDeliveredEnergyKwh = availableEnergyKwh * asset.DischargeEfficiency;
        var actualDeliveredEnergyKwh = result.ProducedSchedule.Windows
            .Sum(w => Math.Max(0, w.TargetPowerKw) * w.Duration.TotalHours);

        Assert.True(result.Run.ObjectiveValue < 0,
            $"expected negative objective from end-of-horizon discharge, got {result.Run.ObjectiveValue}");
        foreach (var window in result.ProducedSchedule.Windows)
        {
            Assert.True(window.TargetPowerKw >= -Tolerance,
                $"unexpected charging at constant price, got {window.TargetPowerKw}");
        }
        Assert.Equal(expectedDeliveredEnergyKwh, actualDeliveredEnergyKwh, precision: 2);
    }

    [Fact]
    public async Task Two_step_price_spread_charges_low_and_discharges_high()
    {
        var optimizer = Build();
        var request = NewRequest(prices: CheapThenExpensive, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
        var w = result.ProducedSchedule!.Windows;
        Assert.True(w[0].TargetPowerKw < -0.5,
            $"step 0 expected charge (negative), got {w[0].TargetPowerKw}");
        Assert.True(w[1].TargetPowerKw > 0.5,
            $"step 1 expected discharge (positive), got {w[1].TargetPowerKw}");
        Assert.True(result.Run.ObjectiveValue < 0,
            $"profitable arbitrage expected negative objective, got {result.Run.ObjectiveValue}");
    }

    [Fact]
    public async Task Power_limits_are_respected()
    {
        var asset = TestFixtures.CreateAsset(maxChargePowerKw: 5, maxDischargePowerKw: 5);
        var optimizer = Build();
        var request = NewRequest(asset, prices: HugeSpread, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        Assert.True(w[0].TargetPowerKw >= -5 - Tolerance && w[0].TargetPowerKw <= 5 + Tolerance);
        Assert.True(w[1].TargetPowerKw >= -5 - Tolerance && w[1].TargetPowerKw <= 5 + Tolerance);
    }

    [Fact]
    public async Task Energy_equals_power_times_delta_with_closed_form_objective_check()
    {
        // LH-OPT-008 unit consistency (review #5): with prices that force
        // the LP to charge at 1 EUR/MWh in step 0 and discharge fully at
        // 200 EUR/MWh in step 1, the closed-form objective is
        //   step0:  +price[0] · p_charge · Δt / 1000     (cost, positive)
        //   step1:  -price[1] · p_discharge · Δt / 1000  (revenue, negative)
        // With Δt = 1 h, p_charge clipped to MaxCharge = 50 kW, the
        // round-trip η = 0.95² ≈ 0.9025 means the LP discharges at most
        // η · 50 = 47.5 kW (limited by SOC band). Asserting both the
        // power saturation AND the total objective in EUR is the only
        // way to catch a kW↔MW or h↔step bug in the objective.
        var asset = TestFixtures.CreateAsset(maxDischargePowerKw: 50, maxChargePowerKw: 50);
        var optimizer = Build();
        var request = NewRequest(asset, LowHighPair, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        // Step 1 should hit max discharge (50 kW).
        Assert.True(w[1].TargetPowerKw >= 50 - Tolerance,
            $"step 1 should saturate max discharge (50 kW), got {w[1].TargetPowerKw}");

        // Closed-form: p_charge = -w[0].TargetPowerKw, p_discharge = w[1].TargetPowerKw.
        var pCharge = Math.Max(0, -w[0].TargetPowerKw);
        var pDischarge = Math.Max(0, w[1].TargetPowerKw);
        var dtHours = 1.0;
        var expectedObjective = LowHighPair[0] * pCharge * dtHours / 1000.0
                                - LowHighPair[1] * pDischarge * dtHours / 1000.0;
        Assert.Equal(expectedObjective, result.Run.ObjectiveValue, precision: 4);
    }

    [Fact]
    public async Task Half_hour_step_doubles_step_count_and_keeps_arbitrage_intact()
    {
        var optimizer = Build();
        // first hour cheap, second hour expensive
        var request = NewRequest(HalfHourPattern, TimeSpan.FromMinutes(30));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.Equal(4, result.ProducedSchedule!.Windows.Count);
        Assert.True(result.ProducedSchedule.Windows[0].TargetPowerKw < 0); // charge
        Assert.True(result.ProducedSchedule.Windows[3].TargetPowerKw > 0); // discharge
        Assert.True(result.Run.ObjectiveValue < 0);
    }

    [Fact]
    public async Task Objective_breakdown_carries_energy_cost_in_eur()
    {
        var optimizer = Build();
        var request = NewRequest(prices: FlatHundred, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.Single(result.Run.ObjectiveBreakdown.Components);
        var component = result.Run.ObjectiveBreakdown.Components[0];
        Assert.Equal("energy_cost", component.Name);
        Assert.Equal("EUR", component.Unit);
        Assert.Equal(result.Run.ObjectiveValue, component.Value);
    }

    [Fact]
    public async Task Schedule_uses_request_market_bid_area_and_versions_above_base()
    {
        // The optimiser no longer queries the schedule repository — the
        // use case populates MarketBidArea + BaseScheduleVersion before
        // calling. Here we pass them directly to verify the optimiser
        // honours them (review #1/#3 boundary).
        var optimizer = Build();
        var request = NewRequestWithIdentity(
            prices: LowHighShort,
            timeStep: TimeSpan.FromHours(1),
            marketBidArea: "AT",
            baseScheduleVersion: 3);

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal("AT", result.ProducedSchedule!.MarketBidArea);
        Assert.Equal(4, result.ProducedSchedule.Version);
    }

    [Fact]
    public async Task Window_count_equals_step_count_for_optimal_status()
    {
        // Plan §Resultatvertrag invariant (review #19).
        var optimizer = Build();
        var request = NewRequest(prices: ThreeStepProfile, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(request.StepCount, result.ProducedSchedule!.Windows.Count);
    }

    [Fact]
    public async Task Explicit_initial_soc_within_band_overrides_midpoint_default_and_solves()
    {
        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions { InitialSocPercent = 30 },
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance);
        var request = NewRequest(LowHighShort, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.NotNull(result.ProducedSchedule);
    }

    [Fact]
    public async Task Time_limit_is_passed_through_without_breaking_a_normal_solve()
    {
        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions { TimeLimit = TimeSpan.FromSeconds(5) },
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance);
        var request = NewRequest(LowHighShort, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
    }

    [Fact]
    public async Task Window_grid_aligns_with_horizon_start_and_step_for_each_window()
    {
        var optimizer = Build();
        var request = NewRequest(prices: ThreeStepProfile, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        var w = result.ProducedSchedule!.Windows;
        Assert.Equal(3, w.Count);
        Assert.Equal(TestFixtures.HorizonStart, w[0].Start);
        Assert.Equal(TestFixtures.HorizonStart + TimeSpan.FromHours(1), w[0].End);
        Assert.Equal(w[0].End, w[1].Start);
        Assert.Equal(w[1].End, w[2].Start);
        Assert.Equal(TestFixtures.HorizonStart + TimeSpan.FromHours(3), w[2].End);
    }

    [Fact]
    public async Task Non_utc_horizon_start_is_normalised_in_produced_windows()
    {
        // Review #4: even if a future caller passes a non-UTC offset,
        // the schedule must come back in canonical UTC form so downstream
        // loaders / persistence don't mis-shift.
        var nonUtcStart = new DateTimeOffset(2026, 5, 7, 14, 0, 0, TimeSpan.FromHours(2));
        var optimizer = Build();
        var command = new ScheduleOptimizationCommand(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: nonUtcStart,
            horizonEnd: nonUtcStart + TimeSpan.FromHours(2),
            timeStep: TimeSpan.FromHours(1),
            pricesPerStep: LowHighShort,
            priceUnit: "EUR/MWh");
        var request = new ScheduleOptimizationRequest(command, "DE-LU", baseScheduleVersion: 0);

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.NotNull(result.ProducedSchedule);
        foreach (var window in result.ProducedSchedule!.Windows)
        {
            Assert.Equal(TimeSpan.Zero, window.Start.Offset);
            Assert.Equal(TimeSpan.Zero, window.End.Offset);
        }
        // Review N1: run-record horizon must be UTC-canonical too,
        // otherwise the audit log carries a different offset than the
        // schedule it points at.
        Assert.Equal(TimeSpan.Zero, result.Run.HorizonStart.Offset);
        Assert.Equal(TimeSpan.Zero, result.Run.HorizonEnd.Offset);
        // Same instant, just normalised: 14:00+02:00 ↔ 12:00Z.
        Assert.Equal(nonUtcStart.UtcDateTime, result.Run.HorizonStart.UtcDateTime);
    }

    [Fact]
    public async Task Trivial_optimum_objective_is_snapped_to_zero()
    {
        // Review #18: a flat-zero price profile makes any charge or
        // discharge worth literally 0 EUR; floating-point noise in the
        // LP could surface as -1e-12, confusing downstream gauges.
        var optimizer = Build();
        var command = new ScheduleOptimizationCommand(
            assetId: "asset-1",
            scheduleType: ScheduleType.DayAhead,
            asset: TestFixtures.CreateAsset(),
            horizonStart: TestFixtures.HorizonStart,
            horizonEnd: TestFixtures.HorizonStart + TimeSpan.FromHours(2),
            timeStep: TimeSpan.FromHours(1),
            pricesPerStep: FlatZero,
            priceUnit: "EUR/MWh");
        var request = new ScheduleOptimizationRequest(command, "DE-LU", baseScheduleVersion: 0);

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        Assert.Equal(0.0, result.Run.ObjectiveValue);
    }

    [Fact]
    public async Task Infeasible_backend_status_yields_no_schedule_and_full_run_payload()
    {
        // Review #7: drives the BuildNonSolutionResult path that the LP
        // never naturally reaches under M2 inputs by overriding the
        // result status before the mapper runs.
        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions(),
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance,
            resultStatusOverride: _ => Solver.ResultStatus.INFEASIBLE);
        var request = NewRequest(prices: LowHighShort, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Infeasible, result.Run.Status);
        Assert.Equal("or-tools-infeasible", result.Run.TerminationReason);
        Assert.Null(result.ProducedSchedule);
        Assert.Null(result.Run.ProducedSchedule);
        Assert.Empty(result.Run.ConstraintViolations);
    }

    [Fact]
    public async Task Unbounded_backend_status_yields_no_schedule_path()
    {
        // Same path as #7 above but for Unbounded — verifies the
        // BuildNonSolutionResult body covers all three non-solution
        // statuses the plan §Resultatvertrag enumerates.
        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions(),
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance,
            resultStatusOverride: _ => Solver.ResultStatus.UNBOUNDED);
        var request = NewRequest(prices: LowHighShort, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Unbounded, result.Run.Status);
        Assert.Null(result.ProducedSchedule);
    }

    private static OrToolsScheduleOptimizer Build() => new(
        new ScheduleSolverOptions(),
        new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
        NullLogger<OrToolsScheduleOptimizer>.Instance);

    private static ScheduleOptimizationRequest NewRequest(
        IReadOnlyList<double> prices,
        TimeSpan timeStep) =>
        NewRequest(TestFixtures.CreateAsset(), prices, timeStep);

    private static ScheduleOptimizationRequest NewRequest(
        BatteryAsset asset,
        IReadOnlyList<double> prices,
        TimeSpan timeStep) =>
        new(NewCommand(asset, prices, timeStep), "DE-LU", baseScheduleVersion: 0);

    private static ScheduleOptimizationRequest NewRequestWithIdentity(
        IReadOnlyList<double> prices,
        TimeSpan timeStep,
        string marketBidArea,
        int baseScheduleVersion) =>
        new(NewCommand(TestFixtures.CreateAsset(), prices, timeStep), marketBidArea, baseScheduleVersion);

    private static ScheduleOptimizationCommand NewCommand(
        BatteryAsset asset,
        IReadOnlyList<double> prices,
        TimeSpan timeStep) => new(
        assetId: "asset-1",
        scheduleType: ScheduleType.DayAhead,
        asset: asset,
        horizonStart: TestFixtures.HorizonStart,
        horizonEnd: TestFixtures.HorizonStart + TimeSpan.FromTicks(timeStep.Ticks * prices.Count),
        timeStep: timeStep,
        pricesPerStep: prices,
        priceUnit: "EUR/MWh");
}
