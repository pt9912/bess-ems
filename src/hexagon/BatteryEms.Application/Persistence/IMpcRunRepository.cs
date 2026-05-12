using BatteryEms.Application.Mpc;

namespace BatteryEms.Application.Persistence;

public interface IMpcRunRepository
{
    Task AppendAsync(MpcRun run, CancellationToken cancellationToken);

    Task<MpcRun?> FindByRequestIdAsync(string mpcRequestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MpcRun>> QueryAsync(
        string assetId,
        DateTimeOffset fromControlCycleTickUtc,
        DateTimeOffset untilControlCycleTickUtc,
        CancellationToken cancellationToken);

    Task<int> CompactAsync(
        MpcRunRetentionPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
