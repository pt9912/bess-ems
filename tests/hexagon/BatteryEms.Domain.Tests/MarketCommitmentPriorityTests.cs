using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class MarketCommitmentPriorityTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(1);

    private static MarketCommitment Commitment(
        MarketType market,
        CommitmentBindingState state,
        double powerKw = 10) =>
        new(market, MarketBidArea: "DE-LU", Start, End, powerKw, Penalty: 0, BindingState: state);

    // --- Rank ---------------------------------------------------------

    [Theory]
    [InlineData(MarketType.RegelLeistung, CommitmentBindingState.Pending, MarketCommitmentPriority.RegelLeistung)]
    [InlineData(MarketType.RegelLeistung, CommitmentBindingState.Binding, MarketCommitmentPriority.RegelLeistung)]
    [InlineData(MarketType.DayAhead, CommitmentBindingState.Binding, MarketCommitmentPriority.BindingMarkt)]
    [InlineData(MarketType.Intraday, CommitmentBindingState.Binding, MarketCommitmentPriority.BindingMarkt)]
    [InlineData(MarketType.Intraday, CommitmentBindingState.Pending, MarketCommitmentPriority.IntradaySchedule)]
    [InlineData(MarketType.DayAhead, CommitmentBindingState.Pending, MarketCommitmentPriority.DayAheadSchedule)]
    public void Rank_maps_market_state_combination_to_lh_mkt_006_slot(
        MarketType market,
        CommitmentBindingState state,
        int expected)
    {
        Assert.Equal(expected, MarketCommitmentPriority.Rank(Commitment(market, state)));
    }

    [Theory]
    [InlineData(CommitmentBindingState.Released)]
    [InlineData(CommitmentBindingState.Violated)]
    public void Rank_filters_released_and_violated_to_not_applicable(CommitmentBindingState state)
    {
        Assert.Equal(MarketCommitmentPriority.NotApplicable,
            MarketCommitmentPriority.Rank(Commitment(MarketType.DayAhead, state)));
        Assert.Equal(MarketCommitmentPriority.NotApplicable,
            MarketCommitmentPriority.Rank(Commitment(MarketType.RegelLeistung, state)));
        Assert.Equal(MarketCommitmentPriority.NotApplicable,
            MarketCommitmentPriority.Rank(Commitment(MarketType.Intraday, state)));
    }

    [Fact]
    public void Rank_throws_for_null_commitment()
    {
        Assert.Throws<ArgumentNullException>(() => MarketCommitmentPriority.Rank(null!));
    }

    // --- SelectHighestPriority ---------------------------------------

    [Fact]
    public void Select_returns_null_for_empty_list()
    {
        var result = MarketCommitmentPriority.SelectHighestPriority(Array.Empty<MarketCommitment>());
        Assert.Null(result);
    }

    [Fact]
    public void Select_returns_null_when_all_commitments_are_released_or_violated()
    {
        var commitments = new[]
        {
            Commitment(MarketType.DayAhead, CommitmentBindingState.Released),
            Commitment(MarketType.Intraday, CommitmentBindingState.Violated),
        };
        Assert.Null(MarketCommitmentPriority.SelectHighestPriority(commitments));
    }

    [Fact]
    public void Select_picks_RegelLeistung_over_binding_DayAhead()
    {
        // LH-MKT-006: #3 RegelLeistung beats #4 verbindliche
        // Marktverpflichtungen — even when RegelLeistung is Pending and
        // DayAhead is Binding (matches the DefaultScheduleTracker
        // mapping convention).
        var rl = Commitment(MarketType.RegelLeistung, CommitmentBindingState.Pending, powerKw: 5);
        var da = Commitment(MarketType.DayAhead, CommitmentBindingState.Binding, powerKw: 20);

        var result = MarketCommitmentPriority.SelectHighestPriority(new[] { da, rl });

        Assert.Same(rl, result);
    }

    [Fact]
    public void Select_picks_binding_DayAhead_over_pending_Intraday()
    {
        var binding = Commitment(MarketType.DayAhead, CommitmentBindingState.Binding, powerKw: 20);
        var pendingIntra = Commitment(MarketType.Intraday, CommitmentBindingState.Pending, powerKw: 30);

        var result = MarketCommitmentPriority.SelectHighestPriority(new[] { pendingIntra, binding });

        Assert.Same(binding, result);
    }

    [Fact]
    public void Select_picks_pending_Intraday_over_pending_DayAhead()
    {
        var intra = Commitment(MarketType.Intraday, CommitmentBindingState.Pending, powerKw: 15);
        var da = Commitment(MarketType.DayAhead, CommitmentBindingState.Pending, powerKw: 5);

        var result = MarketCommitmentPriority.SelectHighestPriority(new[] { da, intra });

        Assert.Same(intra, result);
    }

    [Fact]
    public void Select_skips_violated_to_reach_lower_priority_pending_DayAhead()
    {
        // A Violated commitment is filtered out, so the effective
        // selection falls back to a lower-priority commitment that's
        // still dispatch-relevant.
        var violatedRegel = Commitment(MarketType.RegelLeistung, CommitmentBindingState.Violated, powerKw: 1);
        var releasedBinding = Commitment(MarketType.DayAhead, CommitmentBindingState.Released, powerKw: 2);
        var pendingDa = Commitment(MarketType.DayAhead, CommitmentBindingState.Pending, powerKw: 5);

        var result = MarketCommitmentPriority.SelectHighestPriority(
            new[] { violatedRegel, releasedBinding, pendingDa });

        Assert.Same(pendingDa, result);
    }

    [Fact]
    public void Select_is_stable_on_ties_returning_first_in_input_order()
    {
        var first = Commitment(MarketType.DayAhead, CommitmentBindingState.Binding, powerKw: 10);
        var second = Commitment(MarketType.Intraday, CommitmentBindingState.Binding, powerKw: 20);

        var result = MarketCommitmentPriority.SelectHighestPriority(new[] { first, second });

        Assert.Same(first, result);
    }

    [Fact]
    public void Select_throws_for_null_list()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MarketCommitmentPriority.SelectHighestPriority(null!));
    }

    [Fact]
    public void Select_throws_for_null_commitment_in_list()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MarketCommitmentPriority.SelectHighestPriority(new MarketCommitment[] { null! }));
    }
}
