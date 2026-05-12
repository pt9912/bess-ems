using System;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryEms.Application.Mpc;

// Sub-Slice-A registration stub. The plan §4 row for Sub-Slice A is
// explicit: "In diesem Sub-Slice kein konkreter Solver-Adapter —
// `IMpcModelSolver` wird als `NotImplementedException`-Stub
// registriert; das hält die Schicht-Wartbarkeit beim Cut sauber."
// Calling `SolveAsync` throws; the orchestrator wiring pin asserts the
// exception bubbles up untouched so Sub-Slice B can swap in the real
// adapter without touching the orchestrator. Production hosts must not
// resolve this stub — the Sub-Slice-D activation gate (D-02) only
// registers `IMpcDispatchOptimizer` when `MpcBackend` is a concrete
// value, and that registration brings its own solver.
public sealed class NotImplementedMpcModelSolver : IMpcModelSolver
{
    public Task<MpcTrajectory> SolveAsync(
        MpcState currentState,
        MpcModel model,
        MpcOptions options,
        DateTimeOffset trajectoryAnchor,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "RM-M5-02-A: IMpcModelSolver is contract-only. Sub-Slice B (D-02) provides the QP backend.");
}
