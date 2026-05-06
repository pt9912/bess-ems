using BatteryEms.Application.IO;
using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// LH-PERSIST-002 — every command produced by the control loop or an
// operator endpoint is persisted with its dispatch outcome so the audit
// trail can answer "which value hit the wire, when, and why". CommandId
// is the natural primary key (BatteryCommand.CommandId is unique per
// produced command). Storing the dispatch result alongside the command
// keeps the adapter-limit reason (RM-M1-11) reconstructable post-hoc.
public interface ICommandRepository
{
    Task AppendAsync(BatteryCommand command, CommandDispatchResult dispatch, CancellationToken cancellationToken);

    Task<BatteryCommand?> FindByCommandIdAsync(string commandId, CancellationToken cancellationToken);

    Task<BatteryCommand?> FindLatestAsync(string assetId, CancellationToken cancellationToken);
}
