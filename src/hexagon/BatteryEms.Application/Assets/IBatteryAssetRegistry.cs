using BatteryEms.Domain;

namespace BatteryEms.Application.Assets;

public interface IBatteryAssetRegistry
{
    BatteryAsset? Find(string assetId);

    // Snapshot of every registered asset. The worker iterates this list
    // once per regulation tick to fan out across all assets known at
    // host start-up (RM-M1-19a wires the registry from configuration).
    IReadOnlyList<BatteryAsset> GetAll();
}
