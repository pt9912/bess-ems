using BatteryEms.Domain;

namespace BatteryEms.Application.Control;

// RM-M3-05 driven port for the safety-critical control primitives
// (Constraint, Ramp; PID arrives with RM-M3-13). The cycle's hot
// path calls Compute on whatever IControlKernel is wired by DI; the
// .NET implementation keeps the M1/M2 Constraint+Ramp pipeline as
// the production reference, the native+fallback implementation in
// BatteryEms.Adapters.NativeInterop wraps the C-ABI library and
// falls back to the managed kernel on any native error from a
// validated input.
public interface IControlKernel
{
    KernelResult Compute(KernelInput input);
}

// Inputs the kernel needs. The cycle is responsible for validating
// every field BEFORE constructing this — the kernel itself does
// not re-precheck non-finite values, that path is already a
// safe-stop on the cycle side per the M3-Zielbild "no blind
// fallback with the same invalid values" rule.
public sealed record KernelInput(
    BatteryAsset Asset,
    BatteryTelemetry Telemetry,
    double DispatchTargetActivePowerKw,
    double? PreviousActivePowerKw,
    TimeSpan TimeSinceLastCommand);

// Outcome of the kernel call. ActivePowerKw + Reason carry through
// to the BatteryCommand; WasLimited drives the dispatch reason
// suffix; Source identifies which kernel produced the result so
// observability can distinguish native-success from
// managed-fallback (and the future native-default profile from
// the M1 baseline).
public sealed record KernelResult(
    double ActivePowerKw,
    string Reason,
    bool WasLimited,
    KernelResultSource Source);

public enum KernelResultSource
{
    Managed,
    Native,
    NativeFallbackToManaged,
}
