using System.Threading;
using System.Threading.Tasks;

namespace BatteryEms.Application.Mpc;

// Driven port — D-02 is the open backend-choice axis: a concrete adapter
// arrives in Sub-Slice B against the chosen variant (Local-First QP,
// Sidecar-First `OptimizeMpc` RPC, or Bi-Modal). Sub-Slice A registers
// only the `NotImplementedMpcModelSolver` stub; the orchestrator pins
// for this slice exercise the wiring path up to but not past the solver
// call.
public interface IMpcModelSolver
{
    Task<MpcTrajectory> SolveAsync(
        MpcState currentState,
        MpcModel model,
        MpcOptions options,
        System.DateTimeOffset trajectoryAnchor,
        CancellationToken cancellationToken);
}
