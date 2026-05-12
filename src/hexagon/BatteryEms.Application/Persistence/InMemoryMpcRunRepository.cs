using BatteryEms.Application.Mpc;

namespace BatteryEms.Application.Persistence;

public sealed class InMemoryMpcRunRepository : IMpcRunRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MpcRun> _runs = new(StringComparer.Ordinal);

    public Task AppendAsync(MpcRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_runs.TryAdd(run.MpcRequestId, run))
            {
                throw new InvalidOperationException(
                    $"MpcRun with request id '{run.MpcRequestId}' already exists; runs are append-only.");
            }
        }
        return Task.CompletedTask;
    }

    public Task<MpcRun?> FindByRequestIdAsync(string mpcRequestId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpcRequestId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _runs.TryGetValue(mpcRequestId, out var run);
            return Task.FromResult(run);
        }
    }

    public Task<IReadOnlyList<MpcRun>> QueryAsync(
        string assetId,
        DateTimeOffset fromControlCycleTickUtc,
        DateTimeOffset untilControlCycleTickUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (untilControlCycleTickUtc < fromControlCycleTickUtc)
        {
            throw new ArgumentException(
                "'untilControlCycleTickUtc' must be greater than or equal to 'fromControlCycleTickUtc'.",
                nameof(untilControlCycleTickUtc));
        }
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var rows = _runs.Values
                .Where(r => string.Equals(r.AssetId, assetId, StringComparison.Ordinal)
                    && r.ControlCycleTickUtc >= fromControlCycleTickUtc
                    && r.ControlCycleTickUtc < untilControlCycleTickUtc)
                .OrderBy(r => r.ControlCycleTickUtc)
                .ThenBy(r => r.MpcRequestId, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MpcRun>>(rows);
        }
    }

    public Task<int> CompactAsync(
        MpcRunRetentionPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in _runs.Values.GroupBy(r => r.AssetId, StringComparer.Ordinal))
            {
                foreach (var run in group
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .ThenBy(r => r.MpcRequestId, StringComparer.Ordinal)
                    .Take(policy.KeepLatestPerAsset))
                {
                    keep.Add(run.MpcRequestId);
                }
            }

            var cutoff = policy.MaxAge is null ? DateTimeOffset.MinValue : nowUtc - policy.MaxAge.Value;
            var remove = _runs.Values
                .Where(r => !keep.Contains(r.MpcRequestId) && r.CreatedAtUtc < cutoff)
                .Select(r => r.MpcRequestId)
                .ToArray();
            foreach (var id in remove)
            {
                _runs.Remove(id);
            }
            return Task.FromResult(remove.Length);
        }
    }
}
