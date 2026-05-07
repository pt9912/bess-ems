using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class ScheduleFollowingDispatchOptimizerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly BatteryAsset Asset = new(
        "asset-1", capacityKwh: 100,
        maxChargePowerKw: 50, maxDischargePowerKw: 50,
        minSocPercent: 10, maxSocPercent: 90,
        chargeEfficiency: 0.95, dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 10,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private static readonly BatteryTelemetry Telemetry = new(
        Timestamp: Now,
        AssetId: "asset-1",
        SocPercent: 50,
        SohPercent: 100,
        ActivePowerKw: 0,
        ReactivePowerKvar: 0,
        DcVoltage: 800,
        DcCurrent: 0,
        TemperatureCelsius: 25,
        Available: true,
        FaultStatus: "ok",
        DataQuality: DataQuality.Valid);

    private static MarketCommitment Commitment(
        MarketType market,
        CommitmentBindingState state,
        double powerKw = 10) =>
        new(
            Market: market,
            MarketBidArea: "DE-LU",
            WindowStart: Now,
            WindowEnd: Now.AddHours(1),
            PowerKw: powerKw,
            Penalty: 0,
            BindingState: state);

    private static DispatchRequest Request(params MarketCommitment[] commitments) =>
        new("asset-1", Now, Asset, Telemetry, commitments);

    [Fact]
    public async Task Empty_commitments_falls_back_to_idle()
    {
        var optimizer = new ScheduleFollowingDispatchOptimizer();

        var result = await optimizer.OptimizeAsync(Request(), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.TargetActivePowerKw);
        Assert.Equal("no-active-commitment", result.Reason);
    }

    [Fact]
    public async Task All_released_or_violated_commitments_falls_back_to_idle()
    {
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        var request = Request(
            Commitment(MarketType.DayAhead, CommitmentBindingState.Released),
            Commitment(MarketType.Intraday, CommitmentBindingState.Violated));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.TargetActivePowerKw);
        Assert.Equal("no-active-commitment", result.Reason);
    }

    [Fact]
    public async Task Single_binding_DayAhead_drives_setpoint_to_commitment_power()
    {
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        var request = Request(Commitment(MarketType.DayAhead, CommitmentBindingState.Binding, powerKw: 25));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(25, result.TargetActivePowerKw);
        Assert.Contains("day-ahead", result.Reason, StringComparison.Ordinal);
        Assert.Contains("binding", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Negative_commitment_power_passes_through_as_charge_setpoint()
    {
        // Sign convention: discharge positive, charge negative. The
        // optimiser does not clamp; downstream limiters do.
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        var request = Request(Commitment(MarketType.DayAhead, CommitmentBindingState.Binding, powerKw: -15));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(-15, result.TargetActivePowerKw);
    }

    [Fact]
    public async Task RegelLeistung_takes_precedence_over_binding_DayAhead()
    {
        // LH-MKT-006: #3 RegelLeistung over #4 verbindliche Markt-
        // verpflichtung — even when RegelLeistung is Pending.
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        var request = Request(
            Commitment(MarketType.DayAhead, CommitmentBindingState.Binding, powerKw: 30),
            Commitment(MarketType.RegelLeistung, CommitmentBindingState.Pending, powerKw: 5));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(5, result.TargetActivePowerKw);
        Assert.Contains("regelleistung", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Binding_DayAhead_takes_precedence_over_pending_Intraday()
    {
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        var request = Request(
            Commitment(MarketType.Intraday, CommitmentBindingState.Pending, powerKw: 8),
            Commitment(MarketType.DayAhead, CommitmentBindingState.Binding, powerKw: 22));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(22, result.TargetActivePowerKw);
        Assert.Contains("day-ahead", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reason_string_is_stable_for_replay()
    {
        // Rank tag in the reason is part of the audit-log surface; pin
        // it so dashboards parsing the reason string don't drift on
        // refactors. Pre-RM-M2-01 audit consumers saw the NoOp reason
        // "noop-optimizer"; the new convention is "follows-{market}-
        // {state}-rank-{N}".
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        var request = Request(Commitment(MarketType.RegelLeistung, CommitmentBindingState.Pending, powerKw: 5));

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal("follows-regelleistung-pending-rank-3", result.Reason);
    }

    [Fact]
    public async Task RequestId_is_deterministic_for_identical_inputs()
    {
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        var request = Request(Commitment(MarketType.DayAhead, CommitmentBindingState.Binding));

        var a = await optimizer.OptimizeAsync(request, CancellationToken.None);
        var b = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.Equal(a.RequestId, b.RequestId);
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var optimizer = new ScheduleFollowingDispatchOptimizer();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            optimizer.OptimizeAsync(null!, CancellationToken.None));
    }
}
