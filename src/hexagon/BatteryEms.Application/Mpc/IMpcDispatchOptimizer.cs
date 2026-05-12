using System.Threading;
using System.Threading.Tasks;

namespace BatteryEms.Application.Mpc;

// Driving port — D-01 makes this orthogonal to the M2 `IScheduleOptimizer`
// linie: schedule-level LP runs per re-opt request (minutes/hours);
// MPC runs per sub-second control tick. The Worker's
// `ControlCycleHostedService` (Sub-Slice D wires this) calls
// `NextStepAsync` once per tick on the same cadence it already calls
// `IDispatchOptimizer.OptimizeAsync` today.
//
// Activation gate (D-02): the DI container only registers an
// `IMpcDispatchOptimizer` when `BessHostOptions.MpcBackend` is set to a
// concrete value; the default-bootstrap pin in Sub-Slice D asserts that
// no implementation is resolvable when the slot is null.
public interface IMpcDispatchOptimizer
{
    Task<MpcDispatchResult> NextStepAsync(MpcRequest request, CancellationToken cancellationToken);
}
