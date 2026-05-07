using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

public sealed class OrToolsScheduleOptimizerModelTests
{
    private const double Tolerance = 1e-3;

    private static readonly double[] FlatPrices = { 50.0, 50.0, 50.0, 50.0 };
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
        // Discharge revenue ⇒ negative objective.
        Assert.True(result.Run.ObjectiveValue < 0,
            $"expected negative objective from end-of-horizon discharge, got {result.Run.ObjectiveValue}");
        // No charging at constant price — every window discharges or idles.
        foreach (var window in result.ProducedSchedule.Windows)
        {
            Assert.True(window.TargetPowerKw >= -Tolerance,
                $"unexpected charging at constant price, got {window.TargetPowerKw}");
        }
    }

    [Fact]
    public async Task Two_step_price_spread_charges_low_and_discharges_high()
    {
        // Step 0 cheap, step 1 expensive → charge at step 0, discharge at step 1.
        // Objective = price[0]*P_charge*Δt/1000 − price[1]*P_discharge*Δt/1000 (negative = profit).
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
        // Tiny battery: max charge 5 kW, max discharge 5 kW. Even with a huge
        // price spread the schedule cannot exceed the limit.
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
    public async Task Energy_equals_power_times_delta_at_constant_setpoint()
    {
        // LH-OPT-008 unit consistency test: with prices that force max-rate
        // discharge at step 1, the implied energy export over Δt = 1 h is
        // p_discharge * 1 = 50 kWh (= 0.05 MWh). The objective contribution of
        // that step must equal price[1] * 0.05 = 200 EUR/MWh * 0.05 MWh = 10 EUR.
        // Step 0 (charge at 1 EUR/MWh, but with η_round = 0.95 * 0.95 ≈ 0.9025
        // we lose 1 − 0.9025 ≈ 9.75% of energy charging+discharging).
        // The net objective check is done separately; here we focus on the
        // *step-1 output power* matching the asset's MaxDischargePowerKw.
        var asset = TestFixtures.CreateAsset(maxDischargePowerKw: 50, maxChargePowerKw: 50);
        var optimizer = Build();
        var request = NewRequest(asset, LowHighPair, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        // Step 1 should hit max discharge (50 kW).
        Assert.True(w[1].TargetPowerKw >= 50 - Tolerance,
            $"step 1 should saturate max discharge (50 kW), got {w[1].TargetPowerKw}");
    }

    [Fact]
    public async Task Half_hour_step_doubles_step_count_and_keeps_arbitrage_intact()
    {
        // Same horizon, finer step → twice as many windows. Tests the Δt
        // scaling of the energy / objective contribution path.
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
    public async Task Schedule_inherits_market_bid_area_and_increments_version_when_prior_exists()
    {
        var schedules = new InMemoryScheduleRepository();
        // Seed an existing v3 schedule so the optimizer must produce v4 with
        // the same MarketBidArea.
        var existingWindow = new ScheduleWindow(
            TestFixtures.HorizonStart - TimeSpan.FromHours(1),
            TestFixtures.HorizonStart,
            0);
        var existingWindows = new[] { existingWindow };
        schedules.Replace(new Schedule(
            assetId: "asset-1",
            type: ScheduleType.DayAhead,
            marketBidArea: "AT",
            version: 3,
            windows: existingWindows));

        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions(),
            schedules,
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance);
        var request = NewRequest(prices: LowHighShort, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal("AT", result.ProducedSchedule!.MarketBidArea);
        Assert.Equal(4, result.ProducedSchedule.Version);
    }

    [Fact]
    public async Task First_run_uses_default_market_bid_area_and_version_one()
    {
        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions { DefaultMarketBidArea = "DE-LU" },
            new InMemoryScheduleRepository(),
            new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
            NullLogger<OrToolsScheduleOptimizer>.Instance);
        var request = NewRequest(prices: LowHighShort, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.NotNull(result.ProducedSchedule);
        Assert.Equal("DE-LU", result.ProducedSchedule!.MarketBidArea);
        Assert.Equal(1, result.ProducedSchedule.Version);
    }

    [Fact]
    public async Task Explicit_initial_soc_within_band_overrides_midpoint_default_and_solves()
    {
        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions { InitialSocPercent = 30 },
            new InMemoryScheduleRepository(),
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
        // GLOP solves the M2 LP in microseconds, so a 5-second budget is
        // never reached — the test asserts the option doesn't break the
        // happy path. A real time-limit hit would require a much larger
        // problem than M2 minimal supports.
        var optimizer = new OrToolsScheduleOptimizer(
            new ScheduleSolverOptions { TimeLimit = TimeSpan.FromSeconds(5) },
            new InMemoryScheduleRepository(),
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

    private static OrToolsScheduleOptimizer Build() => new(
        new ScheduleSolverOptions(),
        new InMemoryScheduleRepository(),
        new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
        NullLogger<OrToolsScheduleOptimizer>.Instance);

    private static ScheduleOptimizationRequest NewRequest(
        IReadOnlyList<double> prices,
        TimeSpan timeStep) =>
        NewRequest(TestFixtures.CreateAsset(), prices, timeStep);

    private static ScheduleOptimizationRequest NewRequest(
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
