using BatteryEms.Application.Markets;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.Api;

// Driving port for the /health/regelleistung endpoint (plan-RM-M4-03
// §147). Surfaces the timebase debounce health, the dedupe-store
// health, the production-gate state (master switch + four
// pre-conditions), and the most recent activation outcome that the
// state store recorded.
public interface IRegelleistungHealthQuery
{
    RegelleistungHealthSnapshot Probe();
}

public sealed record RegelleistungHealthSnapshot(
    DateTimeOffset At,
    string Timebase,             // "healthy" | "degraded"
    string DedupeStore,          // "healthy" | "invalid"
    string ProductionGate,       // "enabled" | "disabled"
    RegelleistungPreconditionsSnapshot Preconditions,
    LastActivationSnapshot? LastActivation);

public sealed record RegelleistungPreconditionsSnapshot(
    bool ProductTrust,
    bool TimeSync,
    bool DedupeStoreHealth,
    bool SecurityProfile,
    string ReasonCode);

public sealed class DefaultRegelleistungHealthQuery : IRegelleistungHealthQuery
{
    private readonly ITimebaseHealthSource _timebase;
    private readonly IActivationDedupeStore _dedupe;
    private readonly IProductionPreconditionProvider _preconditions;
    private readonly IRegelleistungActivationStateStore _stateStore;
    private readonly RegelleistungOptions _options;
    private readonly IClock _clock;

    public DefaultRegelleistungHealthQuery(
        ITimebaseHealthSource timebase,
        IActivationDedupeStore dedupe,
        IProductionPreconditionProvider preconditions,
        IRegelleistungActivationStateStore stateStore,
        RegelleistungOptions options,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(timebase);
        ArgumentNullException.ThrowIfNull(dedupe);
        ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _timebase = timebase;
        _dedupe = dedupe;
        _preconditions = preconditions;
        _stateStore = stateStore;
        _options = options;
        _clock = clock;
    }

    public RegelleistungHealthSnapshot Probe()
    {
        var preconditions = _preconditions.Evaluate(_options);
        return new RegelleistungHealthSnapshot(
            At: _clock.UtcNow,
            Timebase: _timebase.Current.Health == TimebaseHealth.Healthy ? "healthy" : "degraded",
            DedupeStore: _dedupe.IsInvalid ? "invalid" : "healthy",
            ProductionGate: _options.ProductionActivationEnabled ? "enabled" : "disabled",
            Preconditions: new RegelleistungPreconditionsSnapshot(
                preconditions.ProductTrust,
                preconditions.TimeSync,
                preconditions.DedupeStoreHealth,
                preconditions.SecurityProfile,
                preconditions.ReasonCode),
            LastActivation: _stateStore.GetLast());
    }
}
