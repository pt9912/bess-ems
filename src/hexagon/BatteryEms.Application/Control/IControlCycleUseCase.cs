using BatteryEms.Domain;

namespace BatteryEms.Application.Control;

public interface IControlCycleUseCase
{
    Task<BatteryCommand> ExecuteAsync(string assetId, CancellationToken cancellationToken);
}
