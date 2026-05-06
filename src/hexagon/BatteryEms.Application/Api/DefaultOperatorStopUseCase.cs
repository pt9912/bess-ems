using BatteryEms.Application.Control;
using BatteryEms.Application.Time;

namespace BatteryEms.Application.Api;

public sealed class DefaultOperatorStopUseCase : IOperatorStopUseCase
{
    private readonly IOperatorStopRegistry _registry;
    private readonly IClock _clock;

    public DefaultOperatorStopUseCase(IOperatorStopRegistry registry, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        _registry = registry;
        _clock = clock;
    }

    public OperatorStopState Execute(OperatorStopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AssetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operator);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);

        var state = new OperatorStopState(
            AssetId: request.AssetId,
            Operator: request.Operator,
            Reason: request.Reason,
            ActivatedAt: _clock.UtcNow);
        _registry.Activate(state);
        return state;
    }
}
