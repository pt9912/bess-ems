using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

// RM-M2-04: configurable objective components. The adapter composes
// energy_cost (existing), degradation_cost (RM-M2-04 new), and
// soc_target_penalty (RM-M2-04 new) into a single LP objective and
// emits one OptimizationObjectiveComponent per active component in the
// breakdown.
public sealed class OrToolsScheduleOptimizerObjectiveComponentTests
{
    private const double Tolerance = 1e-6;

    private static readonly double[] CheapThenExpensive = { 10.0, 200.0 };
    private static readonly double[] FlatPrices = { 50.0, 50.0, 50.0, 50.0 };
    private static readonly double[] Twelve = Enumerable.Range(0, 12).Select(_ => 50.0).ToArray();

    [Fact]
    public async Task Default_options_emit_only_energy_cost_component()
    {
        // Regression: M2-minimal default (no RM-M2-04 components
        // configured) keeps the breakdown at a single energy_cost entry,
        // matching pre-RM-M2-04 behaviour.
        var optimizer = Build(new ScheduleSolverOptions());
        var request = NewRequest(CheapThenExpensive, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var components = result.Run.ObjectiveBreakdown.Components;
        Assert.Single(components);
        Assert.Equal("energy_cost", components[0].Name);
        Assert.Equal("EUR", components[0].Unit);
    }

    // --- degradation_cost --------------------------------------------------

    [Fact]
    public async Task Degradation_cost_appears_in_breakdown_when_configured()
    {
        var optimizer = Build(new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = 0.05 },
        });
        var request = NewRequest(CheapThenExpensive, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        var components = result.Run.ObjectiveBreakdown.Components;
        Assert.Equal(2, components.Count);
        Assert.Equal("energy_cost", components[0].Name);
        Assert.Equal("degradation_cost", components[1].Name);
        Assert.Equal("EUR", components[1].Unit);
    }

    [Fact]
    public async Task Degradation_cost_zero_rate_emits_zero_value_in_breakdown()
    {
        // 0 rate keeps the component visible in the breakdown without
        // changing the LP behaviour — the test verifies (a) the entry
        // is present, (b) its value is exactly 0 even though the LP
        // schedules non-trivial throughput.
        var optimizer = Build(new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = 0 },
        });
        var request = NewRequest(CheapThenExpensive, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        var degradation = result.Run.ObjectiveBreakdown.Components
            .Single(c => c.Name == "degradation_cost");
        Assert.Equal(0.0, degradation.Value);
    }

    [Fact]
    public async Task Degradation_cost_value_matches_throughput_times_rate()
    {
        // Closed-form check: the degradation contribution is
        // EurPerKwhThroughput * Σ (charge[t] + discharge[t]) * Δt. We
        // recompute the throughput from the produced schedule and
        // verify the breakdown value matches within tolerance.
        var rate = 0.05;
        var optimizer = Build(new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = rate },
        });
        var request = NewRequest(CheapThenExpensive, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        var degradation = result.Run.ObjectiveBreakdown.Components
            .Single(c => c.Name == "degradation_cost").Value;
        // Throughput per window = |TargetPowerKw| * Δt because charge
        // and discharge are mutually exclusive in the LP optimum (η<1).
        var expected = rate * result.ProducedSchedule!.Windows
            .Sum(w => Math.Abs(w.TargetPowerKw) * w.Duration.TotalHours);
        Assert.Equal(expected, degradation, precision: 6);
    }

    [Fact]
    public async Task Degradation_cost_high_rate_suppresses_arbitrage_charging()
    {
        // With low degradation, a 1→200 EUR/MWh price spread is
        // arbitraged: the LP charges at price 1, discharges at 200.
        // With a degradation rate above the price spread per kWh, the
        // arbitrage stops being profitable and the LP idles charging
        // entirely (just discharges existing SOC if any).
        var lowDegRate = 0.001;   // 0.1 cent/kWh — well below 0.2 EUR/kWh spread
        var highDegRate = 1.0;    // 1 EUR/kWh — way above the spread

        var lowRequest = NewRequest(CheapThenExpensive, TimeSpan.FromHours(1));
        var highRequest = NewRequest(CheapThenExpensive, TimeSpan.FromHours(1));

        var lowResult = await Build(new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = lowDegRate },
        }).OptimizeAsync(lowRequest, CancellationToken.None);

        var highResult = await Build(new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = highDegRate },
        }).OptimizeAsync(highRequest, CancellationToken.None);

        var lowChargeKwh = lowResult.ProducedSchedule!.Windows
            .Sum(w => Math.Max(0, -w.TargetPowerKw) * w.Duration.TotalHours);
        var highChargeKwh = highResult.ProducedSchedule!.Windows
            .Sum(w => Math.Max(0, -w.TargetPowerKw) * w.Duration.TotalHours);

        Assert.True(lowChargeKwh > 1.0,
            $"low-degradation case should still charge for arbitrage; got {lowChargeKwh} kWh");
        Assert.True(highChargeKwh < Tolerance,
            $"high-degradation case should not charge; got {highChargeKwh} kWh");
    }

    // --- soc_target_penalty -----------------------------------------------

    [Fact]
    public async Task Soc_target_penalty_appears_in_breakdown_when_configured()
    {
        var optimizer = Build(new ScheduleSolverOptions
        {
            SocTargetPenalty = new SocTargetPenaltyOptions
            {
                TargetSocPercent = 50,
                EurPerPercentDeviation = 0.1,
            },
        });
        var request = NewRequest(FlatPrices, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        var components = result.Run.ObjectiveBreakdown.Components;
        Assert.Equal(2, components.Count);
        Assert.Equal("soc_target_penalty", components[1].Name);
        Assert.True(components[1].Value >= 0,
            $"penalty value must be non-negative, got {components[1].Value}");
    }

    [Fact]
    public async Task Soc_target_penalty_drives_schedule_toward_target_under_flat_prices()
    {
        // With flat prices the LP without any penalty empties the
        // battery for end-of-horizon revenue (M2-minimal terminal-SOC
        // is unconstrained — see existing model test). With a high
        // SOC-target penalty, the LP holds SOC near the target instead.
        //
        // Initial SOC = (10+90)/2 = 50%, so with target=50% the LP
        // should hold roughly there; without the penalty it drains.
        var asset = TestFixtures.CreateAsset();
        var capacityKwh = asset.CapacityKwh;
        var targetKwh = 50.0 / 100.0 * capacityKwh;

        var withPenalty = await Build(new ScheduleSolverOptions
        {
            SocTargetPenalty = new SocTargetPenaltyOptions
            {
                TargetSocPercent = 50,
                EurPerPercentDeviation = 100,  // very high to dominate
            },
        }).OptimizeAsync(NewRequest(asset, FlatPrices, TimeSpan.FromHours(1)), CancellationToken.None);

        var withoutPenalty = await Build(new ScheduleSolverOptions())
            .OptimizeAsync(NewRequest(asset, FlatPrices, TimeSpan.FromHours(1)), CancellationToken.None);

        // Approximate end-of-horizon SOC from the schedule's net
        // discharge over the horizon: SOC_end ≈ SOC_init - net_discharge_kwh / η_d.
        static double EndSocKwh(ScheduleOptimizationResult r, BatteryAsset a, double initKwh)
        {
            var netDischargeKwh = r.ProducedSchedule!.Windows
                .Sum(w => w.TargetPowerKw * w.Duration.TotalHours);
            // Approximation: a positive net target draws from SOC at
            // 1/ηD; charging adds at ηC. Sum is dominated by discharge
            // on a flat-price horizon.
            return initKwh - netDischargeKwh / a.DischargeEfficiency;
        }

        var initKwh = (asset.MinSocPercent + asset.MaxSocPercent) / 200.0 * capacityKwh;
        var endWith = EndSocKwh(withPenalty, asset, initKwh);
        var endWithout = EndSocKwh(withoutPenalty, asset, initKwh);

        Assert.True(Math.Abs(endWith - targetKwh) < Math.Abs(endWithout - targetKwh),
            $"penalty should pull SOC toward target {targetKwh} kWh; "
            + $"with-penalty end={endWith}, without-penalty end={endWithout}");
    }

    [Fact]
    public async Task Soc_target_penalty_value_matches_per_step_deviation_sum()
    {
        // Closed-form check: penalty = EurPerPercent * Σ |soc_pct[t] - target_pct|
        // across t = 1..n (initial SOC excluded, as documented). We
        // recompute deviation from the schedule's net energy flow and
        // verify the breakdown matches.
        var asset = TestFixtures.CreateAsset();
        var capacityKwh = asset.CapacityKwh;
        var initKwh = (asset.MinSocPercent + asset.MaxSocPercent) / 200.0 * capacityKwh;
        var targetPercent = 50.0;
        var targetKwh = targetPercent / 100.0 * capacityKwh;
        var penaltyRate = 0.5;

        var optimizer = Build(new ScheduleSolverOptions
        {
            SocTargetPenalty = new SocTargetPenaltyOptions
            {
                TargetSocPercent = targetPercent,
                EurPerPercentDeviation = penaltyRate,
            },
        });
        var request = NewRequest(asset, FlatPrices, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        // Reconstruct end-of-step SOC trajectory.
        var socKwh = initKwh;
        var deviationSum = 0.0;
        foreach (var window in result.ProducedSchedule!.Windows)
        {
            var dt = window.Duration.TotalHours;
            // power kW * dt h = kWh; LP uses charge_kwh = ηC * p_charge * dt,
            // discharge_kwh = p_discharge / ηD * dt. SOC change = charge_kwh -
            // discharge_kwh. With p_charge = -min(target, 0) and
            // p_discharge = max(target, 0):
            var pCharge = Math.Max(0, -window.TargetPowerKw);
            var pDischarge = Math.Max(0, window.TargetPowerKw);
            socKwh += asset.ChargeEfficiency * pCharge * dt;
            socKwh -= pDischarge / asset.DischargeEfficiency * dt;

            var socPercent = socKwh / capacityKwh * 100.0;
            deviationSum += Math.Abs(socPercent - targetPercent);
        }
        var expected = penaltyRate * deviationSum;

        var actual = result.Run.ObjectiveBreakdown.Components
            .Single(c => c.Name == "soc_target_penalty").Value;
        Assert.Equal(expected, actual, precision: 4);
    }

    // --- combined ---------------------------------------------------------

    [Fact]
    public async Task All_three_components_appear_when_all_configured()
    {
        var optimizer = Build(new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = 0.02 },
            SocTargetPenalty = new SocTargetPenaltyOptions
            {
                TargetSocPercent = 50,
                EurPerPercentDeviation = 0.05,
            },
        });
        var request = NewRequest(Twelve, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        var components = result.Run.ObjectiveBreakdown.Components;
        Assert.Equal(3, components.Count);
        Assert.Equal("energy_cost", components[0].Name);
        Assert.Equal("degradation_cost", components[1].Name);
        Assert.Equal("soc_target_penalty", components[2].Name);
    }

    [Fact]
    public async Task Component_values_sum_to_total_objective_within_epsilon()
    {
        // Sanity invariant: the LP minimises the sum of components, so
        // the breakdown must sum to the run-level ObjectiveValue (modulo
        // the snap-to-zero on the total). With a non-trivial setup the
        // total is comfortably away from zero, so the snap doesn't
        // apply and the equality is exact within FP tolerance.
        var optimizer = Build(new ScheduleSolverOptions
        {
            DegradationCost = new DegradationCostOptions { EurPerKwhThroughput = 0.02 },
            SocTargetPenalty = new SocTargetPenaltyOptions
            {
                TargetSocPercent = 60,
                EurPerPercentDeviation = 0.05,
            },
        });
        var request = NewRequest(CheapThenExpensive, TimeSpan.FromHours(1));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        var sum = result.Run.ObjectiveBreakdown.Components.Sum(c => c.Value);
        Assert.Equal(result.Run.ObjectiveValue, sum, precision: 6);
    }

    // --- helpers ----------------------------------------------------------

    private static OrToolsScheduleOptimizer Build(ScheduleSolverOptions options) => new(
        options,
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
        new(
            new ScheduleOptimizationCommand(
                assetId: "asset-1",
                scheduleType: ScheduleType.DayAhead,
                asset: asset,
                horizonStart: TestFixtures.HorizonStart,
                horizonEnd: TestFixtures.HorizonStart + TimeSpan.FromTicks(timeStep.Ticks * prices.Count),
                timeStep: timeStep,
                pricesPerStep: prices,
                priceUnit: "EUR/MWh"),
            "DE-LU",
            baseScheduleVersion: 0);
}
