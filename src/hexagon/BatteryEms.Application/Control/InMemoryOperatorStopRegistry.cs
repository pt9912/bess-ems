using System.Collections.Concurrent;

namespace BatteryEms.Application.Control;

public sealed class InMemoryOperatorStopRegistry : IOperatorStopRegistry
{
    private readonly ConcurrentDictionary<string, OperatorStopState> _byAsset =
        new(StringComparer.Ordinal);

    public OperatorStopState? Find(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _byAsset.TryGetValue(assetId, out var state) ? state : null;
    }

    public void Activate(OperatorStopState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // Latest activation wins. M1 has no Clear endpoint by design
        // (LH-API-006 only requires "stop is honoured"); a fresh
        // operator-stop call simply replaces the recorded reason and
        // operator while keeping the asset in the stop set.
        _byAsset[state.AssetId] = state;
    }
}
