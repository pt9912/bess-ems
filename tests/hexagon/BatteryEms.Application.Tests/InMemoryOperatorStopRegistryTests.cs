using BatteryEms.Application.Control;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryOperatorStopRegistryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Find_returns_null_when_no_stop_recorded()
    {
        var registry = new InMemoryOperatorStopRegistry();
        Assert.Null(registry.Find("asset-1"));
    }

    [Fact]
    public void Activate_then_Find_returns_recorded_state()
    {
        var registry = new InMemoryOperatorStopRegistry();
        var state = new OperatorStopState("asset-1", "operator-1", "manual", Now);

        registry.Activate(state);

        Assert.Equal(state, registry.Find("asset-1"));
    }

    [Fact]
    public void Latest_activation_wins_when_called_twice_for_the_same_asset()
    {
        var registry = new InMemoryOperatorStopRegistry();
        registry.Activate(new OperatorStopState("asset-1", "operator-1", "first", Now));

        var second = new OperatorStopState("asset-1", "operator-2", "second", Now + TimeSpan.FromMinutes(1));
        registry.Activate(second);

        Assert.Equal(second, registry.Find("asset-1"));
    }

    [Fact]
    public void Find_throws_for_blank_assetId()
    {
        var registry = new InMemoryOperatorStopRegistry();
        Assert.Throws<ArgumentException>(() => registry.Find(""));
    }
}
