namespace BatteryEms.Application.Mpc;

// Marker port for an in-process MPC fallback path. The method contract is
// identical to IMpcDispatchOptimizer; the separate type keeps DI from
// resolving the primary backend recursively when a future sidecar MPC
// adapter asks for its local fallback.
public interface IFallbackMpcOptimizer : IMpcDispatchOptimizer
{
}
