using BatteryEms.Domain;

namespace BatteryEms.Application.IO;

public interface IBatteryCommandSink
{
    Task<CommandDispatchResult> WriteAsync(BatteryCommand command, CancellationToken cancellationToken);
}
