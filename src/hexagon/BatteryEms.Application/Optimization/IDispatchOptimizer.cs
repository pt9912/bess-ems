namespace BatteryEms.Application.Optimization;

public interface IDispatchOptimizer
{
    Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken);
}
