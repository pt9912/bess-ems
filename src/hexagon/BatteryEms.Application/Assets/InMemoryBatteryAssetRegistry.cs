using System.Collections.Concurrent;
using BatteryEms.Domain;

namespace BatteryEms.Application.Assets;

public sealed class InMemoryBatteryAssetRegistry : IBatteryAssetRegistry
{
    private readonly ConcurrentDictionary<string, BatteryAsset> _byId = new(StringComparer.Ordinal);

    public InMemoryBatteryAssetRegistry(IEnumerable<BatteryAsset>? seed = null)
    {
        if (seed is null)
        {
            return;
        }

        foreach (var asset in seed)
        {
            _byId[asset.AssetId] = asset;
        }
    }

    public void Register(BatteryAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        _byId[asset.AssetId] = asset;
    }

    public BatteryAsset? Find(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _byId.TryGetValue(assetId, out var asset) ? asset : null;
    }
}
