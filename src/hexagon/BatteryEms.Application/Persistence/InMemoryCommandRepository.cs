using System.Collections.Concurrent;
using BatteryEms.Application.IO;
using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// In-memory drop-in for ICommandRepository. M1 wires this in the API
// host (RM-M1-15a) so the read endpoints have a working backend without
// dragging the Postgres adapter into the driving-adapter boundary; the
// Worker / Composition Root in RM-M1-19 will swap in the Dapper-backed
// repository for production runs.
public sealed class InMemoryCommandRepository : ICommandRepository
{
    private readonly ConcurrentDictionary<string, BatteryCommand> _byCommandId =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BatteryCommand> _latestByAsset =
        new(StringComparer.Ordinal);

    public Task AppendAsync(BatteryCommand command, CommandDispatchResult dispatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dispatch);

        _byCommandId[command.CommandId] = command;
        _latestByAsset.AddOrUpdate(
            command.AssetId,
            command,
            (_, existing) => existing.Timestamp >= command.Timestamp ? existing : command);
        return Task.CompletedTask;
    }

    public Task<BatteryCommand?> FindByCommandIdAsync(string commandId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        return Task.FromResult(_byCommandId.TryGetValue(commandId, out var command) ? command : null);
    }

    public Task<BatteryCommand?> FindLatestAsync(string assetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return Task.FromResult(_latestByAsset.TryGetValue(assetId, out var command) ? command : null);
    }
}
