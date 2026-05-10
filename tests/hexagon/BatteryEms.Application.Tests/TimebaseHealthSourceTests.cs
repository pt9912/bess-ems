using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class TimebaseHealthSourceTests
{
    [Fact]
    public void Initial_current_is_healthy()
    {
        var src = new InMemoryTimebaseHealthSource();
        Assert.Equal(TimebaseHealth.Healthy, src.Current.Health);
    }

    [Fact]
    public void Three_violations_in_window_transition_to_degraded()
    {
        var src = new InMemoryTimebaseHealthSource();

        src.Observe(true);
        src.Observe(true);
        src.Observe(true);

        Assert.Equal(TimebaseHealth.Degraded, src.Current.Health);
    }

    [Fact]
    public void Recover_returns_state_to_healthy()
    {
        var src = new InMemoryTimebaseHealthSource();
        src.Observe(true);
        src.Observe(true);
        src.Observe(true);

        src.Recover();

        Assert.Equal(TimebaseHealth.Healthy, src.Current.Health);
    }

    [Fact]
    public void Stable_observations_keep_state_healthy()
    {
        var src = new InMemoryTimebaseHealthSource();
        for (var i = 0; i < 10; i++) { src.Observe(false); }

        Assert.Equal(TimebaseHealth.Healthy, src.Current.Health);
    }
}
