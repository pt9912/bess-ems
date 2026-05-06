using BatteryEms.Application.Api;
using BatteryEms.Application.Control;
using BatteryEms.Application.Time;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class DefaultOperatorStopUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Execute_writes_state_into_the_registry_with_the_clock_timestamp()
    {
        var registry = new InMemoryOperatorStopRegistry();
        var useCase = new DefaultOperatorStopUseCase(registry, new FixedClock(Now));

        var state = useCase.Execute(new OperatorStopRequest("asset-1", "operator-1", "manual"));

        Assert.Equal("asset-1", state.AssetId);
        Assert.Equal("operator-1", state.Operator);
        Assert.Equal("manual", state.Reason);
        Assert.Equal(Now, state.ActivatedAt);

        // Registry singleton was actually populated.
        Assert.Equal(state, registry.Find("asset-1"));
    }

    [Theory]
    [InlineData("", "op", "reason")]
    [InlineData("asset", "", "reason")]
    [InlineData("asset", "op", "")]
    public void Execute_rejects_blank_required_fields(string assetId, string @operator, string reason)
    {
        var useCase = new DefaultOperatorStopUseCase(new InMemoryOperatorStopRegistry(), new FixedClock(Now));

        Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new OperatorStopRequest(assetId, @operator, reason)));
    }

    [Fact]
    public void Execute_throws_for_null_request()
    {
        var useCase = new DefaultOperatorStopUseCase(new InMemoryOperatorStopRegistry(), new FixedClock(Now));
        Assert.Throws<ArgumentNullException>(() => useCase.Execute(null!));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }
}
