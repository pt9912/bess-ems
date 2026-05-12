using System;
using System.Collections.Generic;

namespace BatteryEms.Application.Mpc;

// Outcome of one MPC control cycle. `IsUsable` mirrors the LP-line
// `HasUsableSolution` flag — a Skip/Fail/Fallback path produces a
// result that callers must read the reason from before reusing the
// trajectory. `Stamps` exposes the D-04 reproducibility surface as an
// opaque `string -> string` dictionary so RM-M5-04 replay can read it
// without the Application layer knowing the persistence schema. The
// dictionary is filled in by Sub-Slice D; today the orchestrator emits
// the model-version and deterministic-mode entries so the contract is
// non-empty from day one and Sub-Slice D's pin can be additive.
public sealed class MpcDispatchResult
{
    public string RequestId { get; }
    public bool IsUsable { get; }
    public string Reason { get; }
    public MpcTrajectory? Trajectory { get; }
    public MpcState? PosteriorState { get; }
    public IReadOnlyDictionary<string, string> Stamps { get; }

    private MpcDispatchResult(
        string requestId,
        bool isUsable,
        string reason,
        MpcTrajectory? trajectory,
        MpcState? posteriorState,
        IReadOnlyDictionary<string, string> stamps)
    {
        RequestId = requestId;
        IsUsable = isUsable;
        Reason = reason;
        Trajectory = trajectory;
        PosteriorState = posteriorState;
        Stamps = stamps;
    }

    public static MpcDispatchResult Usable(
        string requestId,
        MpcTrajectory trajectory,
        MpcState posteriorState,
        IReadOnlyDictionary<string, string> stamps,
        string reason = MpcConstraintReasons.Committed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(posteriorState);
        ArgumentNullException.ThrowIfNull(stamps);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MpcDispatchResult(requestId, true, reason, trajectory, posteriorState, stamps);
    }

    public static MpcDispatchResult NotUsable(
        string requestId,
        string reason,
        IReadOnlyDictionary<string, string> stamps,
        MpcState? posteriorState = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(stamps);
        return new MpcDispatchResult(requestId, false, reason, trajectory: null, posteriorState, stamps);
    }
}
