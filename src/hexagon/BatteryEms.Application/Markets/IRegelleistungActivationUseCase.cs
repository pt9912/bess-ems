using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Application.Markets;

// Driving port for the Regelleistung activation pipeline (plan-RM-M4-
// 03 §147). External source adapters (RM-M4-04 OPC-UA, F-09 MQTT/HTTP)
// drop into this single entry point. The use-case validates the
// activation, applies the production-gate, persists the outcome (audit
// trail), and — when the result is dispatch-relevant — submits to the
// IActivationDispatchSource so the optimizer consults it on the next
// tick.
public interface IRegelleistungActivationUseCase
{
    Task<ActivationOutcome> ReceiveAsync(
        RegelleistungActivation activation,
        CancellationToken cancellationToken = default);
}

// Typed outcome of an activation reception. ReasonCode follows the
// kebab-case canon from ActivationValidationReasons; DispatchRelevant
// mirrors whether the optimizer will see this activation in its next
// tick. Validation rejections (schema/time/timebase/dedupe) are not
// dispatch-relevant by definition; an Accepted validation can still be
// not-dispatch-relevant when the production gate is closed (default)
// or when a pre-condition fails.
public sealed record ActivationOutcome(
    string ReasonCode,
    string Details,
    bool DispatchRelevant);

public sealed partial class DefaultRegelleistungActivationUseCase : IRegelleistungActivationUseCase
{
    private readonly ActivationValidator _validator;
    private readonly ITimebaseHealthSource _timebaseSource;
    private readonly IActivationDispatchSource _dispatchSource;
    private readonly IProductionPreconditionProvider _preconditions;
    private readonly IRegelleistungActivationStateStore _stateStore;
    private readonly RegelleistungOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DefaultRegelleistungActivationUseCase> _logger;

    public DefaultRegelleistungActivationUseCase(
        ActivationValidator validator,
        ITimebaseHealthSource timebaseSource,
        IActivationDispatchSource dispatchSource,
        IProductionPreconditionProvider preconditions,
        IRegelleistungActivationStateStore stateStore,
        RegelleistungOptions options,
        IClock clock,
        ILogger<DefaultRegelleistungActivationUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(timebaseSource);
        ArgumentNullException.ThrowIfNull(dispatchSource);
        ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _validator = validator;
        _timebaseSource = timebaseSource;
        _dispatchSource = dispatchSource;
        _preconditions = preconditions;
        _stateStore = stateStore;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ActivationOutcome> ReceiveAsync(
        RegelleistungActivation activation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();

        var validationResult = await _validator
            .ValidateAsync(activation, _timebaseSource.Current, cancellationToken)
            .ConfigureAwait(false);

        ActivationOutcome outcome;
        if (!validationResult.IsAccepted)
        {
            // Rejected at validation — not dispatch-relevant by
            // definition. The reason code carries the failing step.
            outcome = new ActivationOutcome(
                validationResult.ReasonCode,
                validationResult.Details,
                DispatchRelevant: false);
        }
        else
        {
            outcome = ApplyProductionGate(activation);
        }

        if (outcome.DispatchRelevant)
        {
            _dispatchSource.Submit(activation);
        }

        _stateStore.RecordOutcome(new LastActivationSnapshot(
            SourceId: activation.SourceId,
            ActivationId: activation.ActivationId,
            ReceivedAt: _clock.UtcNow,
            ReasonCode: outcome.ReasonCode,
            DispatchRelevant: outcome.DispatchRelevant,
            Details: outcome.Details));

        // Audit trail (plan-RM-M4-03 §147): every outcome — accepted
        // and rejected — emits a structured log event so the forensic
        // history persists in the host's stdout stream beyond the
        // single-slot in-memory state holder. Persistent DB-backed
        // audit is a follow-up when forensic retention requirements
        // get specified.
        LogActivationOutcome(
            _logger,
            activation.SourceId,
            activation.ActivationId,
            outcome.ReasonCode,
            outcome.DispatchRelevant);

        return outcome;
    }

    [LoggerMessage(EventId = 4100, Level = LogLevel.Information,
        Message = "regelleistung activation outcome: source={SourceId} id={ActivationId} reason={ReasonCode} dispatch_relevant={DispatchRelevant}")]
    private static partial void LogActivationOutcome(
        ILogger logger,
        string sourceId,
        string activationId,
        string reasonCode,
        bool dispatchRelevant);

    private ActivationOutcome ApplyProductionGate(RegelleistungActivation activation)
    {
        // Master switch (D-03): when ProductionActivationEnabled is
        // false (default), every accepted activation is audited but not
        // dispatched — useful for pre-production rollouts where
        // operators want the validation pipeline live without a real
        // dispatch path.
        if (!_options.ProductionActivationEnabled)
        {
            return new ActivationOutcome(
                ActivationValidationReasons.NotDispatchRelevant,
                "ProductionActivationEnabled is false; activation is audited but not dispatched.",
                DispatchRelevant: false);
        }

        // Pre-conditions checked first — plan §147 lists them as the
        // gate that ProductionActivationEnabled=true unlocks. A failure
        // here surfaces the specific failed pre-condition's reason
        // (product-trust, timebase-degraded, dedupe-store-invalid, or
        // security-profile-enforcement-not-wired).
        var preconditions = _preconditions.Evaluate(_options);
        if (!preconditions.IsGreen)
        {
            return new ActivationOutcome(
                preconditions.ReasonCode,
                preconditions.Details,
                DispatchRelevant: false);
        }

        // mFRR fail-closed (D-05): plan-RM-M4-03 wording is "auch bei
        // true und allen Pre-Conditions grün, Product=Mfrr ist immer
        // not-dispatch-relevant". The check fires AFTER pre-conditions
        // so an aFRR run with failing pre-conditions surfaces the
        // pre-condition reason; mFRR runs with green pre-conditions
        // surface the mFRR-modelable reason. Productive
        // MOLS-/MARI-Aktivierung is F-08, beyond M4 scope.
        if (activation.Product == ReserveProduct.Mfrr)
        {
            return new ActivationOutcome(
                ActivationValidationReasons.NotDispatchRelevant,
                "mFRR activation is modelable but never dispatched in M4 (D-05).",
                DispatchRelevant: false);
        }

        return new ActivationOutcome(
            ActivationValidationReasons.Accepted,
            "activation passed validation and production-gate; submitted to dispatch source.",
            DispatchRelevant: true);
    }
}
