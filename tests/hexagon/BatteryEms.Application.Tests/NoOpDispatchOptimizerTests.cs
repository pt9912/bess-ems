using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class NoOpDispatchOptimizerTests
{
    [Fact]
    public async Task Returns_idle_dispatch_for_any_request()
    {
        IDispatchOptimizer optimizer = new NoOpDispatchOptimizer();
        var request = new DispatchRequest(
            AssetId: "asset-1",
            RequestTime: TestFixtures.Now,
            Asset: TestFixtures.CreateAsset(),
            CurrentTelemetry: TestFixtures.CreateTelemetry(),
            Commitments: Array.Empty<MarketCommitment>());

        var result = await optimizer.OptimizeAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.TargetActivePowerKw);
        Assert.Equal("noop-optimizer", result.Reason);
    }
}
