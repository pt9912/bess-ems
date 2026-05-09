using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

// RM-M4-02 LH-MKT-004: held reserve bands are deducted from the per-
// step charge/discharge caps before the LP is built. FCR-symmetric
// withholds capacity on both sides; AFRR/MFRR-Up only on discharge;
// AFRR/MFRR-Down only on charge. The optimiser must (a) honour the
// effective caps, (b) keep the M2 happy path bit-identical when no
// reserves are held, and (c) terminate cleanly when reserves over-
// commit the asset's nameplate.
public sealed class OrToolsScheduleOptimizerReserveTests
{
    private const double Tolerance = 1e-3;

    // Wide spread to maximise the LP's incentive to push power against
    // the cap — reveals whether the cap actually held.
    private static readonly double[] HugeSpread = { 1.0, 1000.0 };
    // Negative-cheap-then-expensive: step 0 has a negative price so the
    // LP earns by charging (paid to consume). That pins charging to the
    // cap regardless of round-trip efficiency or initial SOC; without
    // negative prices, idle+discharge is always >= charge+discharge in
    // profit (the SOC pool already supplies the discharge), so the LP
    // wouldn't push charge to the cap.
    private static readonly double[] PaidToChargeThenExpensive = { -100.0, 200.0 };

    [Fact]
    public async Task Empty_reserves_path_is_bit_identical_to_no_reserves()
    {
        // Regression pin — adding the optional Reserves parameter must
        // not perturb the M2 happy path.
        var optimizer = Build();
        var withoutReserves = NewRequest(HugeSpread, TimeSpan.FromHours(1), reserves: null);
        var withEmptyReserves = NewRequest(
            HugeSpread, TimeSpan.FromHours(1), reserves: Array.Empty<ReserveBand>());

        var resultA = await optimizer.OptimizeAsync(withoutReserves, CancellationToken.None);
        var resultB = await optimizer.OptimizeAsync(withEmptyReserves, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, resultA.Run.Status);
        Assert.Equal(OptimizationSolverStatus.Optimal, resultB.Run.Status);
        var aWindows = resultA.ProducedSchedule!.Windows;
        var bWindows = resultB.ProducedSchedule!.Windows;
        Assert.Equal(aWindows.Count, bWindows.Count);
        for (var i = 0; i < aWindows.Count; i++)
        {
            Assert.Equal(aWindows[i].TargetPowerKw, bWindows[i].TargetPowerKw, precision: 6);
        }
    }

    [Fact]
    public async Task Fcr_symmetric_reserve_clamps_both_charge_and_discharge_caps()
    {
        // Asset can do ±50 kW; FCR=10 kW symmetric over the full
        // horizon must reduce both caps to 40 kW. The cheap-then-
        // expensive arbitrage profile would otherwise drive the LP
        // straight to -50/+50; with the reserve it can only reach
        // -40/+40.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var fcr = new ReserveBand(
            asset.AssetId, ReserveProduct.Fcr, ReserveDirection.Symmetric,
            horizon.Start, horizon.End, 10);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { fcr });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        // Charge step must be at most -40 kW (40 kW charging in our
        // sign convention; charging is reported as negative target).
        Assert.True(w[0].TargetPowerKw >= -40 - Tolerance,
            $"FCR=10 should clamp charge cap to 40 kW, got {w[0].TargetPowerKw}");
        Assert.True(w[1].TargetPowerKw <= 40 + Tolerance,
            $"FCR=10 should clamp discharge cap to 40 kW, got {w[1].TargetPowerKw}");
        // The LP's incentive is to push to the cap; verify it actually
        // sits AT the cap so the cap is binding (not just unused).
        Assert.True(w[0].TargetPowerKw <= -40 + 1.0,
            $"with FCR=10 LP should charge at the cap, got {w[0].TargetPowerKw}");
        Assert.True(w[1].TargetPowerKw >= 40 - 1.0,
            $"with FCR=10 LP should discharge at the cap, got {w[1].TargetPowerKw}");
    }

    [Fact]
    public async Task Afrr_up_reserve_clamps_only_the_discharge_cap()
    {
        // AFRR-Up=10 kW must reduce the discharge cap to 40 kW. Charge
        // cap is unchanged at 50 kW — i.e. the asset can still charge
        // at full speed. Profile: cheap-then-expensive.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var up = new ReserveBand(
            asset.AssetId, ReserveProduct.Afrr, ReserveDirection.Up,
            horizon.Start, horizon.End, 10);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { up });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        // Charge can still hit -50 kW (no Down reserve held).
        Assert.True(w[0].TargetPowerKw >= -50 - Tolerance,
            $"AFRR-Up should NOT clamp charge cap, got {w[0].TargetPowerKw}");
        Assert.True(w[0].TargetPowerKw <= -50 + 1.0,
            $"with cheap step 0 LP should charge to -50 kW, got {w[0].TargetPowerKw}");
        // Discharge clamped to 40 kW.
        Assert.True(w[1].TargetPowerKw <= 40 + Tolerance,
            $"AFRR-Up=10 should clamp discharge cap to 40 kW, got {w[1].TargetPowerKw}");
        Assert.True(w[1].TargetPowerKw >= 40 - 1.0,
            $"with AFRR-Up=10 LP should discharge at the cap, got {w[1].TargetPowerKw}");
    }

    [Fact]
    public async Task Afrr_down_reserve_clamps_only_the_charge_cap()
    {
        // AFRR-Down=10 kW must reduce charge cap to 40 kW. Discharge
        // cap unchanged at 50 kW.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var down = new ReserveBand(
            asset.AssetId, ReserveProduct.Afrr, ReserveDirection.Down,
            horizon.Start, horizon.End, 10);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { down });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        // Charge clamped to 40 kW (i.e. -40 in target convention).
        Assert.True(w[0].TargetPowerKw >= -40 - Tolerance,
            $"AFRR-Down=10 should clamp charge cap to 40 kW, got {w[0].TargetPowerKw}");
        Assert.True(w[0].TargetPowerKw <= -40 + 1.0,
            $"with AFRR-Down=10 LP should charge at the cap, got {w[0].TargetPowerKw}");
        // Discharge unrestricted to 50 kW.
        Assert.True(w[1].TargetPowerKw <= 50 + Tolerance,
            $"AFRR-Down should NOT clamp discharge cap, got {w[1].TargetPowerKw}");
    }

    [Fact]
    public async Task Mfrr_reserve_uses_same_constraint_shape_as_afrr()
    {
        // mFRR is structurally identical to AFRR for the LP — a
        // directional capacity withholding. RM-M4-02 demands "im
        // Produktmodell und in Reservierungsdaten abbildbar, ohne
        // produktive Aktivierung zu verlangen".
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var mfrrUp = new ReserveBand(
            asset.AssetId, ReserveProduct.Mfrr, ReserveDirection.Up,
            horizon.Start, horizon.End, 15);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { mfrrUp });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        Assert.True(w[1].TargetPowerKw <= 35 + Tolerance,
            $"MFRR-Up=15 should clamp discharge cap to 35 kW, got {w[1].TargetPowerKw}");
    }

    [Fact]
    public async Task Multiple_same_direction_reserves_sum()
    {
        // FCR=5 + AFRR-Up=3 over the same step both withhold from the
        // discharge side; effective discharge cap = 50 - 5 - 3 = 42.
        // FCR also clamps charge to 50 - 5 = 45.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var fcr = new ReserveBand(
            asset.AssetId, ReserveProduct.Fcr, ReserveDirection.Symmetric,
            horizon.Start, horizon.End, 5);
        var afrrUp = new ReserveBand(
            asset.AssetId, ReserveProduct.Afrr, ReserveDirection.Up,
            horizon.Start, horizon.End, 3);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { fcr, afrrUp });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        Assert.True(w[0].TargetPowerKw >= -45 - Tolerance,
            $"FCR=5 should clamp charge cap to 45 kW, got {w[0].TargetPowerKw}");
        Assert.True(w[1].TargetPowerKw <= 42 + Tolerance,
            $"FCR=5 + AFRR-Up=3 should clamp discharge cap to 42 kW, got {w[1].TargetPowerKw}");
    }

    [Fact]
    public async Task Reserve_outside_horizon_window_does_not_constrain()
    {
        // A reserve whose window does not overlap the horizon must
        // leave the optimiser unconstrained — no per-step deduction.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        // Band lives entirely AFTER the horizon ends.
        var farFuture = new ReserveBand(
            asset.AssetId, ReserveProduct.Fcr, ReserveDirection.Symmetric,
            horizon.End, horizon.End + TimeSpan.FromHours(1), 25);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { farFuture });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        // No clamping — full 50 kW available in both directions.
        Assert.True(w[0].TargetPowerKw <= -50 + 1.0,
            $"out-of-horizon reserve should not clamp, got {w[0].TargetPowerKw}");
        Assert.True(w[1].TargetPowerKw >= 50 - 1.0,
            $"out-of-horizon reserve should not clamp, got {w[1].TargetPowerKw}");
    }

    [Fact]
    public async Task Reserve_for_other_asset_id_does_not_constrain()
    {
        // Defensive filter — if the use case feeds a band whose AssetId
        // does not match the request's asset, the optimiser ignores it.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var foreign = new ReserveBand(
            "OTHER-ASSET", ReserveProduct.Fcr, ReserveDirection.Symmetric,
            horizon.Start, horizon.End, 25);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { foreign });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        Assert.True(w[0].TargetPowerKw <= -50 + 1.0,
            $"foreign-asset reserve should not clamp, got {w[0].TargetPowerKw}");
    }

    [Fact]
    public async Task Reserve_exceeding_capacity_terminates_with_dedicated_code()
    {
        // FCR=60 on a 50 kW asset: effective cap goes negative. Operator-
        // actionable signal beats LP-infeasible.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var overcommit = new ReserveBand(
            asset.AssetId, ReserveProduct.Fcr, ReserveDirection.Symmetric,
            horizon.Start, horizon.End, 60);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { overcommit });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Failed, result.Run.Status);
        Assert.Equal("reserve-exceeds-capacity", result.Run.TerminationCode);
        Assert.Null(result.ProducedSchedule);
    }

    [Fact]
    public async Task Half_open_band_does_not_apply_to_step_starting_at_band_end()
    {
        // Band [horizon.Start, horizon.Start+1h) covers only step 0 in
        // a two-step horizon. Step 1 starts at horizon.Start+1h, which
        // is the EXCLUDED end of the band. With cheap-then-expensive
        // prices the LP would want to discharge at full 50 kW in step
        // 1 — verify the band's exclusion at the boundary lets it.
        // capacityKwh=1000 puts the SOC band wide enough that the
        // power cap is the binding constraint, not the SOC floor/ceiling.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var firstStepOnly = new ReserveBand(
            asset.AssetId, ReserveProduct.Afrr, ReserveDirection.Up,
            horizon.Start, horizon.Start + TimeSpan.FromHours(1), 25);

        var optimizer = Build();
        var request = NewRequest(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), reserves: new[] { firstStepOnly });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        // Step 1 sees no reserve — the discharge cap is the full 50 kW.
        Assert.True(w[1].TargetPowerKw >= 50 - 1.0,
            $"step 1 (band-excluded) should discharge at 50 kW, got {w[1].TargetPowerKw}");
    }

    [Theory]
    [InlineData(ScheduleType.DayAhead)]
    [InlineData(ScheduleType.Intraday)]
    public async Task Reserve_clamp_holds_for_both_schedule_types(ScheduleType scheduleType)
    {
        // Plan-DoD pin: "Day-Ahead- und Intraday-Optimierung Reserve-
        // Bänder nicht verletzen". The optimiser is currently
        // ScheduleType-agnostic in its LP body — this test inoculates
        // against a future ScheduleType-conditional branch silently
        // invalidating Intraday coverage.
        var asset = TestFixtures.CreateAsset(capacityKwh: 1000, maxChargePowerKw: 50, maxDischargePowerKw: 50);
        var horizon = NewHorizon(TimeSpan.FromHours(1), PaidToChargeThenExpensive.Length);
        var fcr = new ReserveBand(
            asset.AssetId, ReserveProduct.Fcr, ReserveDirection.Symmetric,
            horizon.Start, horizon.End, 10);

        var optimizer = Build();
        var request = new ScheduleOptimizationRequest(
            NewCommand(asset, PaidToChargeThenExpensive, TimeSpan.FromHours(1), scheduleType),
            "DE-LU", baseScheduleVersion: 0, reserves: new[] { fcr });
        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(OptimizationSolverStatus.Optimal, result.Run.Status);
        var w = result.ProducedSchedule!.Windows;
        Assert.True(w[0].TargetPowerKw >= -40 - Tolerance,
            $"{scheduleType}: FCR=10 should clamp charge cap to 40 kW, got {w[0].TargetPowerKw}");
        Assert.True(w[1].TargetPowerKw <= 40 + Tolerance,
            $"{scheduleType}: FCR=10 should clamp discharge cap to 40 kW, got {w[1].TargetPowerKw}");
    }

    private static OrToolsScheduleOptimizer Build() => new(
        new ScheduleSolverOptions(),
        new TestFixtures.FrozenClock(TestFixtures.HorizonStart),
        NullLogger<OrToolsScheduleOptimizer>.Instance);

    private static (DateTimeOffset Start, DateTimeOffset End) NewHorizon(TimeSpan timeStep, int n) =>
        (TestFixtures.HorizonStart,
            TestFixtures.HorizonStart + TimeSpan.FromTicks(timeStep.Ticks * n));

    private static ScheduleOptimizationRequest NewRequest(
        IReadOnlyList<double> prices,
        TimeSpan timeStep,
        IReadOnlyList<ReserveBand>? reserves) =>
        NewRequest(TestFixtures.CreateAsset(), prices, timeStep, reserves);

    private static ScheduleOptimizationRequest NewRequest(
        BatteryAsset asset,
        IReadOnlyList<double> prices,
        TimeSpan timeStep,
        IReadOnlyList<ReserveBand>? reserves) =>
        new(NewCommand(asset, prices, timeStep), "DE-LU", baseScheduleVersion: 0, reserves);

    private static ScheduleOptimizationCommand NewCommand(
        BatteryAsset asset,
        IReadOnlyList<double> prices,
        TimeSpan timeStep,
        ScheduleType scheduleType = ScheduleType.DayAhead) => new(
        assetId: asset.AssetId,
        scheduleType: scheduleType,
        asset: asset,
        horizonStart: TestFixtures.HorizonStart,
        horizonEnd: TestFixtures.HorizonStart + TimeSpan.FromTicks(timeStep.Ticks * prices.Count),
        timeStep: timeStep,
        pricesPerStep: prices,
        priceUnit: "EUR/MWh");
}
