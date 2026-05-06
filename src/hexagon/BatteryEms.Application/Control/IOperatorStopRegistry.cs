namespace BatteryEms.Application.Control;

// LH-API-006 driving-meets-control surface: the API endpoint marks an
// asset as operator-stopped, and the ControlCycleUseCase reads the same
// registry on every cycle and short-circuits to a safe stop while the
// flag is set. Lives in Application.Control because the control loop is
// the primary consumer; the API write path is a thin shell on top.
//
// The registry is in-memory in M1 — a process restart lifts every stop.
// docs/user/persistence.md §3 calls that out as the documented M1 limit;
// RM-M1-19 will move stops to persistent storage when the Worker /
// Composition Root materialises.
public interface IOperatorStopRegistry
{
    OperatorStopState? Find(string assetId);

    void Activate(OperatorStopState state);
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OperatorStopState(
    string AssetId,
    string Operator,
    string Reason,
    DateTimeOffset ActivatedAt);
