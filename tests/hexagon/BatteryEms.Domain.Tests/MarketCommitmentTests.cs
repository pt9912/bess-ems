using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class MarketCommitmentTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Covers_returns_true_for_moments_inside_window_excluding_end()
    {
        var commitment = new MarketCommitment(
            Market: MarketType.DayAhead,
            WindowStart: Start,
            WindowEnd: Start + TimeSpan.FromHours(1),
            PowerKw: 30,
            Penalty: 100,
            BindingState: CommitmentBindingState.Binding);

        Assert.True(commitment.Covers(Start));
        Assert.True(commitment.Covers(Start + TimeSpan.FromMinutes(30)));
        Assert.False(commitment.Covers(Start + TimeSpan.FromHours(1)));
        Assert.False(commitment.Covers(Start - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Duration_is_window_end_minus_start()
    {
        var commitment = new MarketCommitment(
            MarketType.DayAhead,
            Start,
            Start + TimeSpan.FromMinutes(15),
            10,
            0,
            CommitmentBindingState.Pending);

        Assert.Equal(TimeSpan.FromMinutes(15), commitment.Duration);
    }
}
