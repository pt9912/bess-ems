using System.Collections.Concurrent;
using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// Process-local drop-in for IOptimizationRunRepository. The host
// (RM-M1-19a) wires this in tests and headless smoke runs so the
// schedule-optimizer use case has a working backend without dragging
// the Postgres adapter into the driving-adapter boundary; production
// swaps in DapperOptimizationRunRepository in RM-M2-OP-06.
//
// Append-only by contract — LH-OPT-009 / LH-PERSIST-007 treat runs as
// immutable history, so re-appending a RunId is rejected.
public sealed class InMemoryOptimizationRunRepository : IOptimizationRunRepository
{
    private readonly ConcurrentDictionary<Guid, OptimizationRun> _byId = new();

    public Task AppendAsync(OptimizationRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!_byId.TryAdd(run.RunId, run))
        {
            throw new InvalidOperationException(
                $"OptimizationRun with id '{run.RunId}' already exists; runs are append-only.");
        }
        return Task.CompletedTask;
    }

    public Task<OptimizationRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_byId.TryGetValue(runId, out var run) ? run : null);
    }

    public Task<IReadOnlyList<OptimizationRun>> QueryAsync(
        string assetId,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (until < from)
        {
            throw new ArgumentException(
                "'until' must be greater than or equal to 'from'.", nameof(until));
        }

        IReadOnlyList<OptimizationRun> result = _byId.Values
            .Where(r => r.AssetId == assetId && r.CreatedAt >= from && r.CreatedAt < until)
            .OrderBy(r => r.CreatedAt)
            .ToArray();
        return Task.FromResult(result);
    }
}
