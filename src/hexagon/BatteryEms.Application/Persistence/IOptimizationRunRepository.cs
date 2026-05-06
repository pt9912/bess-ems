using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// LH-PERSIST-007 — every horizon-level optimisation run is stored
// append-only with its full LH-OPT-009 payload (RunId, solver status,
// objective breakdown, produced schedule reference, …) so a run can
// be explained, audited and — wherever the solver permits —
// reproduced. The query surface mirrors what the API and the M2
// run-history view need:
//
//   AppendAsync   — write a finalised run; runs are immutable after
//                   the solver returned, mutations go via "append a
//                   new run with newer Inputs".
//   FindByIdAsync — single-run lookup for the API by RunId.
//   QueryAsync    — half-open [from, until) range query per asset,
//                   ordered by CreatedAt ascending. Same convention
//                   as ITelemetryRepository / IOperatorAuditLog.
public interface IOptimizationRunRepository
{
    Task AppendAsync(OptimizationRun run, CancellationToken cancellationToken);

    Task<OptimizationRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OptimizationRun>> QueryAsync(
        string assetId,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken);
}
