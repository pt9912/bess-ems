using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.IO;

// Default IBatteryCommandSink for hosts that have not yet wired a real
// Modbus/MQTT sink. Acknowledges every command without dispatching it
// anywhere — keeps the regulation loop running without forcing every
// integration test to provide a real adapter.
public sealed class NoOpBatteryCommandSink : IBatteryCommandSink
{
    private readonly IClock _clock;

    public NoOpBatteryCommandSink(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public Task<CommandDispatchResult> WriteAsync(BatteryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Task.FromResult(CommandDispatchResult.Ok(_clock.UtcNow, "noop-sink"));
    }
}
