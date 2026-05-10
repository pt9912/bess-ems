using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driven port carrying the four production-gate pre-conditions
// (plan-RM-M4-03 D-03). The use-case calls Evaluate when an
// activation has passed validation and ProductionActivationEnabled is
// true — any failed pre-condition keeps the outcome
// not-dispatch-relevant with a structured reason.
//
//   - ProductTrust:     RegelleistungOptions.ProductTrustEstablished — the
//                       boolean operator-trust stamp; defaults false so it
//                       cannot be silently true.
//   - TimeSync:         TimebaseDebounceState.Health == Healthy.
//   - DedupeStoreHealth: IActivationDedupeStore.IsInvalid == false.
//   - SecurityProfile:   today fail-closed (security-profile-enforcement-
//                        not-wired) until F-12 lands a real cross-adapter
//                        security profile signal. The default provider
//                        returns this reason for every activation, which
//                        means the production-code path is intentionally
//                        not productive-shippable until F-12 fires.
//
// Tests inject HealthyProductionPreconditionProvider when they need
// to drive the dispatch-relevant path through the production gate
// without F-12 wired (analog to the NoOpActivationDispatchSource
// pattern from D-09).
public interface IProductionPreconditionProvider
{
    ProductionPreconditionStatus Evaluate(RegelleistungOptions options);
}

// Result of a pre-condition evaluation. ReasonCode is empty (or
// ActivationValidationReasons.Accepted) when all four checks are green;
// otherwise it's the kebab-case code of the failing check.
public sealed record ProductionPreconditionStatus(
    bool ProductTrust,
    bool TimeSync,
    bool DedupeStoreHealth,
    bool SecurityProfile,
    string ReasonCode,
    string Details)
{
    public bool IsGreen => ProductTrust && TimeSync && DedupeStoreHealth && SecurityProfile;
}

// Production-code provider — fails closed on the security-profile
// pre-condition until F-12 wires a real RuntimeProfile / Security-
// Profile health signal across adapters.
public sealed class DefaultProductionPreconditionProvider : IProductionPreconditionProvider
{
    private readonly ITimebaseHealthSource _timebase;
    private readonly IActivationDedupeStore _dedupeStore;

    public DefaultProductionPreconditionProvider(
        ITimebaseHealthSource timebase,
        IActivationDedupeStore dedupeStore)
    {
        ArgumentNullException.ThrowIfNull(timebase);
        ArgumentNullException.ThrowIfNull(dedupeStore);
        _timebase = timebase;
        _dedupeStore = dedupeStore;
    }

    public ProductionPreconditionStatus Evaluate(RegelleistungOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var productTrust = options.ProductTrustEstablished;
        var timeSync = _timebase.Current.Health == TimebaseHealth.Healthy;
        var dedupeHealth = !_dedupeStore.IsInvalid;

        if (!productTrust)
        {
            return new ProductionPreconditionStatus(
                productTrust, timeSync, dedupeHealth, SecurityProfile: false,
                ReasonCode: ActivationValidationReasons.ProductTrustNotEstablished,
                Details: "product-trust not established (set Regelleistung:ProductTrustEstablished=true).");
        }
        if (!timeSync)
        {
            return new ProductionPreconditionStatus(
                productTrust, timeSync, dedupeHealth, SecurityProfile: false,
                ReasonCode: ActivationValidationReasons.TimebaseDegraded,
                Details: "timebase debounce state is Degraded.");
        }
        if (!dedupeHealth)
        {
            return new ProductionPreconditionStatus(
                productTrust, timeSync, dedupeHealth, SecurityProfile: false,
                ReasonCode: ActivationValidationReasons.DedupeStoreInvalid,
                Details: "dedupe store is in an invalid state.");
        }

        // SecurityProfile is intentionally fail-closed today: there is
        // no cross-adapter security-profile-grün signal yet. F-12 lands
        // a real RuntimeProfile / Security-Profile health source; the
        // production-code provider then either short-circuits earlier
        // (if the signal is in-band on this provider) or the host swaps
        // in a different IProductionPreconditionProvider implementation.
        // Until then, even with the first three checks green the
        // production gate stays closed — which is exactly the
        // "intentionally not productive-shippable" state the plan
        // (D-03) calls out.
        return new ProductionPreconditionStatus(
            productTrust, timeSync, dedupeHealth, SecurityProfile: false,
            ReasonCode: ActivationValidationReasons.SecurityProfileEnforcementNotWired,
            Details: "security profile enforcement not wired (waiting on F-12).");
    }
}

// Test stub: returns all-green so the aFRR profile tests in Sub-Slice
// E can drive the dispatch-relevant path without F-12 wired (plan
// D-03 test-override pin).
public sealed class HealthyProductionPreconditionProvider : IProductionPreconditionProvider
{
    public ProductionPreconditionStatus Evaluate(RegelleistungOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProductionPreconditionStatus(
            ProductTrust: true,
            TimeSync: true,
            DedupeStoreHealth: true,
            SecurityProfile: true,
            ReasonCode: ActivationValidationReasons.Accepted,
            Details: "test stub — all pre-conditions green.");
    }
}
