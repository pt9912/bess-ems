using BatteryEms.Domain;

namespace BatteryEms.Application.Assets;

public interface IBatteryAssetRegistry
{
    BatteryAsset? Find(string assetId);
}
