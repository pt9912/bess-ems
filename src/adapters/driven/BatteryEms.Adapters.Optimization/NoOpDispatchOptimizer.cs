using BatteryEms.Application.Optimization;

namespace BatteryEms.Adapters.Optimization;

public sealed class NoOpDispatchOptimizer : IDispatchOptimizer
{
    public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestId = $"noop-{request.RequestTime.ToUnixTimeMilliseconds()}-{request.AssetId}";
        return Task.FromResult(DispatchResult.Idle(requestId, "noop-optimizer"));
    }
}
