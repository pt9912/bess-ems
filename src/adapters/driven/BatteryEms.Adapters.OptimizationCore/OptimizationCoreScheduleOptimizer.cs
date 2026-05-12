using System.Security.Cryptography;
using System.Text;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OptimizationCore;

// `IScheduleOptimizer`-Adapter, der die M2-`ScheduleOptimizationRequest`
// in gRPC-Protobuf-Form übersetzt, gegen den optimization-core-Sidecar
// fährt und das Result via `OptimizationCoreStatusMapper` in M2-
// `ScheduleOptimizationResult` zurückübersetzt (plan-RM-M5-01 D-01).
//
// Wire-Flow:
//   1. ConnectAsync (idempotent, baut den GrpcChannel beim ersten Call)
//   2. Health-Probe (lazy, einmal pro Lifetime des Adapters)
//   3. Version + Feature-Negotiation; bei Mismatch → kein Optimize
//   4. Optimize-Server-Streaming, finale `OptimizeResult` einsammeln
//   5. Mapper-Lookup + M2-`ScheduleOptimizationResult`-Konstruktion
//
// Fallback-Pfade gemäß plan-RM-M5 §Fallback-Matrix:
//   - DeadlineExceeded / Unavailable / Cancelled / InvalidArgument /
//     andere RpcExceptions ⇒ Status-Mapper liefert das Outcome, der
//     Adapter baut einen Failed-`OptimizationRun` ohne neue Schedule-
//     Version.
//   - Sidecar liefert Infeasible / Unbounded / TimeLimit-ohne-Lösung
//     ⇒ Failed-Run ohne Schedule.
//   - Sidecar liefert Optimal / Feasible (incl. TimeLimit+usable) ⇒
//     Solution-Run mit produzierter `Schedule`.
//
// **Sub-Slice-C-Vorbehalte:** Persistenter Idempotency-Store, Plan-
// Gültigkeits-Check (Zeitindex / MaxFallbackScheduleAge / Kontext-
// Stempel / Telemetrie-Drift), Local-Optimizer-Fallback bei
// `or_tools`-Backend → alles RM-M5-01-C.
// CA1506-Schwelle (96) ist überschritten weil dieser Adapter der
// hexagonale Wire-Endpunkt für den optimization-core-Sidecar ist:
// gRPC-Generated-Types + Domain-Model + Application-Driven-Ports
// (IOptimizationIdempotencyStore, IFallbackScheduleOptimizer,
// IFallbackPlanValidator) + Mapper. Das Coupling ist Pattern-immanent;
// alle Inputs sind bewusste Driven-Port-Kopplungen aus plan-RM-M5
// §Komponenten.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability", "CA1506",
    Justification = "Sidecar-Adapter-Top-Level: koppelt M2-Domain, gRPC-Wire-Typen, Idempotency-Store, Fallback-Optimizer und Plan-Validator — Pattern-immanent.")]
internal sealed class OptimizationCoreScheduleOptimizer : IScheduleOptimizer, IDisposable
{
    private const string SolverName = "optimization-core";

    private readonly OptimizationCoreClient _client;
    private readonly OptimizationCoreOptions _options;
    private readonly IOptimizationIdempotencyStore _idempotencyStore;
    private readonly IClock _clock;
    private readonly ILogger<OptimizationCoreScheduleOptimizer> _logger;
    private readonly IOptimizationCoreMetrics _metrics;
    // RM-M5-01-C Korrektur-Pass: optionaler lokaler Fallback-Optimizer
    // (z. B. OR-Tools) + Plan-Validator gemäß plan-RM-M5 §Fallback-Matrix
    // Zeile „Timeout/Deadline oder Unavailable vor Ergebnis". Beide
    // sind via DI optional — fehlt der Fallback, gilt no_valid_plan +
    // Safe-Stop.
    private readonly IFallbackScheduleOptimizer? _fallbackOptimizer;
    private readonly IFallbackPlanValidator? _planValidator;
    private readonly SemaphoreSlim _versionProbeGate = new(1, 1);
    private bool _versionProbeDone;
    private bool _disposed;

    public OptimizationCoreScheduleOptimizer(
        OptimizationCoreClient client,
        OptimizationCoreOptions options,
        IOptimizationIdempotencyStore idempotencyStore,
        IClock clock,
        ILogger<OptimizationCoreScheduleOptimizer> logger,
        IFallbackScheduleOptimizer? fallbackOptimizer = null,
        IFallbackPlanValidator? planValidator = null,
        IOptimizationCoreMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(idempotencyStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid(logger);
        // Plan-Validator muss vorhanden sein wenn ein Fallback konfiguriert
        // ist — sonst ginge der validator-check stillschweigend baden,
        // was gegen plan-RM-M5 §Fallback-Plan-Gueltigkeit verstößt.
        if (fallbackOptimizer is not null && planValidator is null)
        {
            throw new InvalidOperationException(
                "optimization-core-fallback-without-validator: an "
                + "IFallbackScheduleOptimizer was registered but no "
                + "IFallbackPlanValidator. The validator is mandatory "
                + "when a local fallback is active (plan-RM-M5 "
                + "§Fallback-Plan-Gueltigkeit).");
        }

        _client = client;
        _options = options;
        _idempotencyStore = idempotencyStore;
        _clock = clock;
        _logger = logger;
        _metrics = metrics ?? NoOpOptimizationCoreMetrics.Instance;
        _fallbackOptimizer = fallbackOptimizer;
        _planValidator = planValidator;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability", "CA1506",
        Justification = "Sidecar-Adapter-Top-Level: koppelt M2-Domain-Modell, gRPC-Wire-Typen und Idempotency-Store — die Kopplung ist intrinsisch dem Adapter-Pattern.")]
    public async Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var horizonStartUtc = request.HorizonStart.ToUniversalTime();
        var startedAt = _clock.UtcNow;

        // Plan-RM-M5 §Request-Idempotenz: deterministische `request_id`
        // aus den fachlichen Identitäts-Feldern; TryBegin atomar pre-
        // Sidecar. Existiert ein finaler Eintrag mit dieser ID, wird
        // der Sidecar-Call übersprungen und ein `late_response_ignored`-
        // Failed-Run zurückgegeben (Worker liest den ursprünglichen
        // OptimizationRun via IOptimizationRunRepository für den
        // echten Verlauf).
        var requestId = ComputeRequestId(request);
        var beginResult = await _idempotencyStore.TryBeginAsync(
            requestId, startedAt, cancellationToken).ConfigureAwait(false);
        if (!beginResult.IsNewlyCreated)
        {
            return HandleExistingIdempotencyEntry(
                request, horizonStartUtc, beginResult.Entry, startedAt);
        }

        try
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await EnsureContractCompatibleAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ContractIncompatibleException ex)
        {
            _metrics.RecordSidecarHealth("contract_incompatible");
            OptimizationCoreLog.LogContractIncompatible(_logger, ex.Detail);
            var contractFailed = BuildFailedResult(
                request,
                horizonStartUtc,
                OptimizationCoreStatusMapper.ClassifyContractIncompatible(),
                terminationCode: "contract-incompatible",
                terminationDetail: ex.Detail,
                elapsed: _clock.UtcNow - startedAt);
            return await FinalizeAndReturnAsync(
                requestId, contractFailed,
                OptimizationTerminalState.FailedNoActivation,
                "contract-incompatible",
                FallbackSource.NoActivation,
                FallbackReason.ContractIncompatible,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            _metrics.RecordSidecarHealth("unavailable");
            // Plan-RM-M5 §Fallback-Matrix: Sidecar-Connect-/Health-/
            // Version-Pfad scheitert ⇒ Lokaler Optimierer-Fallback wenn
            // konfiguriert; sonst no_valid_plan + Safe-Stop.
            var outcome = OptimizationCoreStatusMapper.ClassifyTransport(ex.StatusCode);
            var fallbackOutcome = await TryRunFallbackAsync(
                request, horizonStartUtc, cancellationToken).ConfigureAwait(false);
            if (fallbackOutcome is not null)
            {
                return await FinalizeAndReturnAsync(
                    requestId, fallbackOutcome,
                    OptimizationTerminalState.FallbackCommitted,
                    "local-optimizer-fallback-committed",
                    FallbackSource.LocalOptimizer,
                    outcome.FallbackReason,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            var transportFailed = BuildFailedResult(
                request,
                horizonStartUtc,
                outcome,
                terminationCode: ex.StatusCode.ToString(),
                terminationDetail: NormalizeTerminationDetail(ex.Status.Detail),
                elapsed: _clock.UtcNow - startedAt);
            return await FinalizeAndReturnAsync(
                requestId, transportFailed,
                OptimizationTerminalState.FailedNoActivation,
                MapTerminalReason(outcome.FallbackReason),
                ResolveFallbackSource(outcome.FallbackSource, OptimizationTerminalState.FailedNoActivation),
                outcome.FallbackReason,
                cancellationToken)
                .ConfigureAwait(false);
        }

        var protoRequest = BuildProtoRequest(request);
        var deadline = DateTime.UtcNow + _options.RequestDeadline;

        try
        {
            using var call = _client.Client.Optimize(
                protoRequest, deadline: deadline, cancellationToken: cancellationToken);

            Grpc.V1.OptimizeResult? final = null;
            await foreach (var update in call.ResponseStream
                .ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (update.UpdateCase)
                {
                    case Grpc.V1.OptimizeUpdate.UpdateOneofCase.Progress:
                        OptimizationCoreLog.LogProgress(
                            _logger,
                            update.Progress.StepIndex,
                            update.Progress.ObjectiveSoFar);
                        break;
                    case Grpc.V1.OptimizeUpdate.UpdateOneofCase.Result:
                        final = update.Result;
                        break;
                    default:
                        break;
                }
            }

            if (final is null)
            {
                // Stream-Crash mid-Optimize ⇒ Fallback-Versuch wenn
                // verfügbar (plan-RM-M5 §Fallback-Matrix „Timeout/
                // Deadline oder Unavailable vor Ergebnis").
                var fallbackOutcome = await TryRunFallbackAsync(
                    request, horizonStartUtc, cancellationToken).ConfigureAwait(false);
                if (fallbackOutcome is not null)
                {
                    return await FinalizeAndReturnAsync(
                        requestId, fallbackOutcome,
                        OptimizationTerminalState.FallbackCommitted,
                        "local-optimizer-fallback-committed",
                        FallbackSource.LocalOptimizer,
                        FallbackReason.TransportInternalError,
                        cancellationToken)
                        .ConfigureAwait(false);
                }
                var streamFailed = BuildFailedResult(
                    request,
                    horizonStartUtc,
                    new OptimizationCoreOutcome(
                        Status: OptimizationSolverStatus.Failed,
                        FallbackSource: FallbackSource.FromMatrix,
                        FallbackReason: FallbackReason.TransportInternalError,
                        PersistSchedule: false),
                    terminationCode: "stream-closed-without-result",
                    terminationDetail: null,
                    elapsed: _clock.UtcNow - startedAt);
                return await FinalizeAndReturnAsync(
                    requestId, streamFailed,
                    OptimizationTerminalState.FailedNoActivation,
                    "transport-internal-error",
                    FallbackSource.NoActivation,
                    FallbackReason.TransportInternalError,
                    cancellationToken).ConfigureAwait(false);
            }

            var resultOutcome = OptimizationCoreStatusMapper.ClassifyResult(
                final.SolverStatus, final.HasUsableSolution);
            var built = BuildResult(request, horizonStartUtc, final, resultOutcome,
                elapsed: _clock.UtcNow - startedAt);
            // Wenn das Schedule persistiert wird (sidecar-result) →
            // SidecarCommitted; sonst (Infeasible/Unbounded/Invalid-
            // Trajectory) → FailedNoActivation.
            var (state, reason) = built.ProducedSchedule is not null
                ? (OptimizationTerminalState.SidecarCommitted, "sidecar-committed")
                : (OptimizationTerminalState.FailedNoActivation,
                   MapTerminalReason(resultOutcome.FallbackReason));
            var fallbackSource = built.ProducedSchedule is not null
                ? FallbackSource.SidecarResult
                : ResolveFallbackSource(resultOutcome.FallbackSource, state);
            var fallbackReason = built.ProducedSchedule is not null
                ? FallbackReason.None
                : ResolveFallbackReasonForBuiltResult(resultOutcome, built);
            return await FinalizeAndReturnAsync(
                requestId, built, state, reason, fallbackSource, fallbackReason, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            _metrics.RecordSidecarHealth("unavailable");
            // Sidecar-RpcException während des Optimize-Streams ⇒
            // Fallback-Versuch wenn verfügbar (plan-RM-M5 §Fallback-
            // Matrix). Status-Mapper-Outcome wird nur verwendet wenn
            // kein Fallback registriert oder Fallback fehlschlägt.
            var outcome = OptimizationCoreStatusMapper.ClassifyTransport(ex.StatusCode);
            var fallbackOutcome = await TryRunFallbackAsync(
                request, horizonStartUtc, cancellationToken).ConfigureAwait(false);
            if (fallbackOutcome is not null)
            {
                return await FinalizeAndReturnAsync(
                    requestId, fallbackOutcome,
                    OptimizationTerminalState.FallbackCommitted,
                    "local-optimizer-fallback-committed",
                    FallbackSource.LocalOptimizer,
                    outcome.FallbackReason,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            var rpcFailed = BuildFailedResult(
                request,
                horizonStartUtc,
                outcome,
                terminationCode: ex.StatusCode.ToString(),
                terminationDetail: NormalizeTerminationDetail(ex.Status.Detail),
                elapsed: _clock.UtcNow - startedAt);
            return await FinalizeAndReturnAsync(
                requestId, rpcFailed,
                OptimizationTerminalState.FailedNoActivation,
                MapTerminalReason(outcome.FallbackReason),
                ResolveFallbackSource(outcome.FallbackSource, OptimizationTerminalState.FailedNoActivation),
                outcome.FallbackReason,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller-initiated Cancel mid-stream. Idempotency-Eintrag
            // wird als Cancelled finalisiert (plan-RM-M5 §Atomare
            // Finalisierung: einer der vier Terminalzustände), damit
            // ein späterer Retry mit derselben request_id den Status
            // sieht statt einen zweiten Sidecar-Call zu schicken.
            var cancelledOutcome = new OptimizationCoreOutcome(
                Status: OptimizationSolverStatus.Failed,
                FallbackSource: FallbackSource.FromMatrix,
                FallbackReason: FallbackReason.TransportCancelled,
                PersistSchedule: false);
            var cancelled = BuildFailedResult(
                request, horizonStartUtc, cancelledOutcome,
                terminationCode: "Cancelled",
                terminationDetail: "caller-cancelled-mid-optimize",
                elapsed: _clock.UtcNow - startedAt);
            return await FinalizeAndReturnAsync(
                requestId, cancelled,
                OptimizationTerminalState.Cancelled,
                "transport-cancelled",
                FallbackSource.NoActivation,
                FallbackReason.TransportCancelled,
                cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Plan-RM-M5 §Fallback-Matrix Zeile „Timeout/Deadline oder
    // Unavailable vor Ergebnis": versuche den lokalen Optimierer-
    // Fallback (falls registriert), validiere den produzierten Plan
    // gegen den 4-Achsen-Check und liefere ein `FallbackCommitted`-
    // taugliches `ScheduleOptimizationResult` zurück. Liefert `null`
    // wenn kein Fallback verfügbar ist, der Fallback selbst scheitert,
    // kein Schedule produziert oder die Validation fehlschlägt — der
    // Caller geht dann auf den `no_valid_plan`-Pfad.
    //
    // Wichtig: `OperationCanceledException` propagieren wir bewusst
    // weiter; der Caller hat einen eigenen Cancelled-Pfad mit
    // korrekter Idempotency-Finalisierung. Alle anderen Fallback-
    // Exceptions werden geloggt und zu `null` reduziert (fail-closed).
    private async Task<ScheduleOptimizationResult?> TryRunFallbackAsync(
        ScheduleOptimizationRequest request,
        DateTimeOffset horizonStartUtc,
        CancellationToken cancellationToken)
    {
        if (_fallbackOptimizer is null || _planValidator is null)
        {
            return null;
        }

        OptimizationCoreLog.LogFallbackAttempt(_logger, request.AssetId);
        ScheduleOptimizationResult fallbackResult;
        try
        {
            fallbackResult = await _fallbackOptimizer.OptimizeAsync(
                request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Fail-closed: jeder andere Fallback-Fehler ⇒ no-valid-plan
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptimizationCoreLog.LogFallbackFailed(_logger, request.AssetId, ex);
            return null;
        }

        if (fallbackResult.ProducedSchedule is null)
        {
            // Fallback-Optimizer hat selbst keinen brauchbaren Plan
            // produziert (Infeasible/Failed). Kein Schedule → kein
            // Validator-Aufruf nötig.
            OptimizationCoreLog.LogFallbackProducedNoSchedule(
                _logger, request.AssetId, fallbackResult.Run.TerminationCode);
            return null;
        }

        // Plan-RM-M5 §Fallback-Plan-Gueltigkeit: Kontext-Stempel +
        // Horizon-Alignment + MaxAge + Telemetrie-Drift. Adapter hat
        // keinen Telemetrie-Snapshot im Scope — Drift-Achse wird
        // dadurch im Validator skip'd (siehe DefaultFallbackPlanValidator
        // §CheckTelemetryDrift). Die 3 anderen Achsen laufen aktiv.
        var candidate = new FallbackPlanCandidate(
            Schedule: fallbackResult.ProducedSchedule,
            CreatedAtUtc: _clock.UtcNow);
        var context = new FallbackPlanContext(
            AssetId: request.AssetId,
            ScheduleType: request.ScheduleType,
            CurrentTickUtc: _clock.UtcNow,
            HorizonStart: horizonStartUtc,
            HorizonEnd: request.HorizonEnd.ToUniversalTime(),
            TimeStep: request.TimeStep,
            MarketBidArea: request.MarketBidArea,
            Asset: request.Asset,
            CurrentTelemetry: null);
        var validation = _planValidator.Validate(candidate, context);
        if (!validation.IsValid)
        {
            OptimizationCoreLog.LogFallbackRejected(
                _logger, request.AssetId,
                validation.Reason, validation.Detail ?? string.Empty);
            return null;
        }

        OptimizationCoreLog.LogFallbackCommitted(
            _logger, request.AssetId, fallbackResult.Run.SolverName);
        return fallbackResult;
    }

    // Plan-RM-M5 §Fallback-Taxonomie: kebab-case-Reason aus dem
    // Mapper-Enum für die Persistenz im Idempotency-Store.
    private static string MapTerminalReason(FallbackReason reason) => reason switch
    {
        FallbackReason.None => "none",
        FallbackReason.DeadlineExceeded => "deadline-exceeded",
        FallbackReason.SidecarUnavailable => "sidecar-unavailable",
        FallbackReason.TransportCancelled => "transport-cancelled",
        FallbackReason.TransportInternalError => "transport-internal-error",
        FallbackReason.InvalidRequest => "invalid-request",
        FallbackReason.SolverInfeasible => "solver-infeasible",
        FallbackReason.SolverUnbounded => "solver-unbounded",
        FallbackReason.SolverTimeLimit => "solver-time-limit",
        FallbackReason.SolverIterationLimit => "solver-iteration-limit",
        FallbackReason.NoValidPlan => "no-valid-plan",
        FallbackReason.FallbackPlanExpired => "fallback-plan-expired",
        FallbackReason.FallbackContextMismatch => "fallback-context-mismatch",
        FallbackReason.FallbackTelemetryDrift => "fallback-telemetry-drift",
        FallbackReason.InvalidSnapshot => "invalid-snapshot",
        FallbackReason.InvalidMpcState => "invalid-mpc-state",
        FallbackReason.ContractIncompatible => "contract-incompatible",
        FallbackReason.UnauthorizedClient => "unauthorized-client",
        FallbackReason.DuplicateRequest => "duplicate-request",
        FallbackReason.LateResponseIgnored => "late-response-ignored",
        _ => "transport-internal-error",
    };

    private static string MapFallbackSource(FallbackSource source) => source switch
    {
        FallbackSource.None => "none",
        FallbackSource.SidecarResult => "sidecar_result",
        FallbackSource.LocalOptimizer => "local_optimizer",
        FallbackSource.LastValidSchedule => "last_valid_schedule",
        FallbackSource.SafeStop => "safe_stop",
        FallbackSource.NoActivation => "no_activation",
        // `FromMatrix` is an internal mapper placeholder; metrics must
        // expose the canonical taxonomy after the adapter resolved the
        // path, never the placeholder.
        FallbackSource.FromMatrix => "no_activation",
        _ => "no_activation",
    };

    private static string MapFallbackReason(FallbackReason reason) => reason switch
    {
        FallbackReason.None => "none",
        FallbackReason.DeadlineExceeded => "deadline_exceeded",
        FallbackReason.SidecarUnavailable => "sidecar_unavailable",
        FallbackReason.TransportCancelled => "transport_cancelled",
        FallbackReason.TransportInternalError => "transport_internal_error",
        FallbackReason.InvalidRequest => "invalid_request",
        FallbackReason.SolverInfeasible => "solver_infeasible",
        FallbackReason.SolverUnbounded => "solver_unbounded",
        FallbackReason.SolverTimeLimit => "solver_time_limit",
        FallbackReason.SolverIterationLimit => "solver_iteration_limit",
        FallbackReason.NoValidPlan => "no_valid_plan",
        FallbackReason.FallbackPlanExpired => "fallback_plan_expired",
        FallbackReason.FallbackContextMismatch => "fallback_context_mismatch",
        FallbackReason.FallbackTelemetryDrift => "fallback_telemetry_drift",
        FallbackReason.InvalidSnapshot => "invalid_snapshot",
        FallbackReason.InvalidMpcState => "invalid_mpc_state",
        FallbackReason.ContractIncompatible => "contract_incompatible",
        FallbackReason.UnauthorizedClient => "unauthorized_client",
        FallbackReason.DuplicateRequest => "duplicate_request",
        FallbackReason.LateResponseIgnored => "late_response_ignored",
        _ => "transport_internal_error",
    };

    private static FallbackSource ResolveFallbackSource(
        FallbackSource source,
        OptimizationTerminalState terminalState)
    {
        if (source != FallbackSource.FromMatrix)
        {
            return source;
        }

        return terminalState switch
        {
            OptimizationTerminalState.FallbackCommitted => FallbackSource.LocalOptimizer,
            OptimizationTerminalState.SidecarCommitted => FallbackSource.SidecarResult,
            _ => FallbackSource.NoActivation,
        };
    }

    private static FallbackReason ResolveFallbackReasonForBuiltResult(
        OptimizationCoreOutcome outcome,
        ScheduleOptimizationResult result)
    {
        if (outcome.PersistSchedule && result.ProducedSchedule is null)
        {
            return FallbackReason.TransportInternalError;
        }

        return outcome.FallbackReason;
    }

    // Lazy: Health + Version-Probe einmal pro Adapter-Lifetime
    // ausführen. Bei inkompatibler Version → ContractIncompatibleException
    // (statt RpcException) damit der äußere Catch sie nicht als
    // Transport-Fehler reklassifiziert.
    private async Task EnsureContractCompatibleAsync(CancellationToken cancellationToken)
    {
        if (_versionProbeDone) { return; }
        await _versionProbeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_versionProbeDone) { return; }

            // Health-Probe: kurzer Roundtrip; ein nicht-`SERVING`-Status
            // wird als Unavailable behandelt (Caller-Catch reklassifiziert).
            var healthDeadline = DateTime.UtcNow + _options.ConnectTimeout;
            var health = await _client.Client.HealthAsync(
                new Grpc.V1.HealthRequest(),
                deadline: healthDeadline,
                cancellationToken: cancellationToken);
            if (health.Status != Grpc.V1.HealthResponse.Types.Status.Serving)
            {
                _metrics.RecordSidecarHealth("not_serving");
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    $"sidecar-not-serving: status={health.Status}"));
            }
            _metrics.RecordSidecarHealth("serving");

            var versionDeadline = DateTime.UtcNow + _options.ConnectTimeout;
            var version = await _client.Client.VersionAsync(
                new Grpc.V1.VersionRequest(),
                deadline: versionDeadline,
                cancellationToken: cancellationToken);

            if (!VersionCompatible(
                _options.ExpectedContractVersion,
                version.MinCompatibleVersion,
                version.MaxCompatibleVersion))
            {
                throw new ContractIncompatibleException(
                    $"contract-version-mismatch: expected={_options.ExpectedContractVersion} "
                    + $"sidecar-range=[{version.MinCompatibleVersion}, "
                    + $"{version.MaxCompatibleVersion}]");
            }

            foreach (var required in _options.RequiredFeatures)
            {
                if (!version.Features.Contains(required))
                {
                    throw new ContractIncompatibleException(
                        $"required-feature-missing: '{required}' not in sidecar features "
                        + $"[{string.Join(",", version.Features)}]");
                }
            }

            _versionProbeDone = true;
        }
        finally
        {
            _versionProbeGate.Release();
        }
    }

    // SemVer-Range-Check: expected muss in [min, max] liegen. Defensive
    // gegen leere Strings (Pre-Contract-v1-Sidecars) → InComp.
    private static bool VersionCompatible(string expected, string min, string max)
    {
        if (!Version.TryParse(NormalizeVersion(expected), out var exp)) { return false; }
        if (!Version.TryParse(NormalizeVersion(min), out var minV)) { return false; }
        if (!Version.TryParse(NormalizeVersion(max), out var maxV)) { return false; }
        return exp >= minV && exp <= maxV;
    }

    private static string NormalizeVersion(string s)
    {
        // System.Version erwartet 2-4 Komponenten; "1.0.0" ist 3,
        // OK. SemVer-Pre-Release-Suffixe schneiden wir am `-` ab.
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        return dash >= 0 ? s[..dash] : s;
    }

    private static Grpc.V1.OptimizeRequest BuildProtoRequest(
        ScheduleOptimizationRequest request)
    {
        var horizonStart = request.HorizonStart.ToUniversalTime();
        var horizonEnd = request.HorizonEnd.ToUniversalTime();
        var proto = new Grpc.V1.OptimizeRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            AssetId = request.AssetId,
            ScheduleType = MapScheduleType(request.ScheduleType),
            HorizonStart = Timestamp.FromDateTimeOffset(horizonStart),
            HorizonEnd = Timestamp.FromDateTimeOffset(horizonEnd),
            TimeStep = Duration.FromTimeSpan(request.TimeStep),
            PriceUnit = request.PriceUnit ?? string.Empty,
            MarketBidArea = request.MarketBidArea,
            BaseScheduleVersion = request.BaseScheduleVersion,
            Asset = MapAsset(request.Asset),
        };
        if (request.PricesPerStep is { } prices)
        {
            proto.PricesPerStep.AddRange(prices);
        }
        foreach (var reserve in request.Reserves)
        {
            proto.Reserves.Add(MapReserve(reserve));
        }
        return proto;
    }

    private static Grpc.V1.ScheduleType MapScheduleType(ScheduleType type) => type switch
    {
        ScheduleType.DayAhead => Grpc.V1.ScheduleType.DayAhead,
        ScheduleType.Intraday => Grpc.V1.ScheduleType.Intraday,
        ScheduleType.RegelLeistungReserve => Grpc.V1.ScheduleType.RegelleistungReserve,
        _ => Grpc.V1.ScheduleType.Unspecified,
    };

    private static Grpc.V1.AssetCapabilities MapAsset(BatteryAsset asset) => new()
    {
        AssetId = asset.AssetId,
        CapacityKwh = asset.CapacityKwh,
        MaxChargePowerKw = asset.MaxChargePowerKw,
        MaxDischargePowerKw = asset.MaxDischargePowerKw,
        MinSocPercent = asset.MinSocPercent,
        MaxSocPercent = asset.MaxSocPercent,
        ChargeEfficiency = asset.ChargeEfficiency,
        DischargeEfficiency = asset.DischargeEfficiency,
        MaxRampKwPerSecond = asset.MaxRampKwPerSecond,
        MinOperatingTemperatureCelsius = asset.MinOperatingTemperatureCelsius,
        MaxOperatingTemperatureCelsius = asset.MaxOperatingTemperatureCelsius,
    };

    private static Grpc.V1.ReserveBand MapReserve(ReserveBand band) => new()
    {
        Product = band.Product switch
        {
            ReserveProduct.Fcr => Grpc.V1.ReserveBand.Types.Product.Fcr,
            ReserveProduct.Afrr => Grpc.V1.ReserveBand.Types.Product.Afrr,
            ReserveProduct.Mfrr => Grpc.V1.ReserveBand.Types.Product.Mfrr,
            _ => Grpc.V1.ReserveBand.Types.Product.Unspecified,
        },
        Direction = band.Direction switch
        {
            ReserveDirection.Symmetric => Grpc.V1.ReserveBand.Types.Direction.Symmetric,
            ReserveDirection.Up => Grpc.V1.ReserveBand.Types.Direction.Up,
            ReserveDirection.Down => Grpc.V1.ReserveBand.Types.Direction.Down,
            _ => Grpc.V1.ReserveBand.Types.Direction.Unspecified,
        },
        WindowStart = Timestamp.FromDateTimeOffset(band.Start.ToUniversalTime()),
        WindowEnd = Timestamp.FromDateTimeOffset(band.End.ToUniversalTime()),
        PowerKw = band.PowerKw,
    };

    private ScheduleOptimizationResult BuildResult(
        ScheduleOptimizationRequest request,
        DateTimeOffset horizonStartUtc,
        Grpc.V1.OptimizeResult result,
        OptimizationCoreOutcome outcome,
        TimeSpan elapsed)
    {
        var solverRuntime = result.SolverRuntime?.ToTimeSpan() ?? elapsed;
        var breakdown = BuildObjectiveBreakdown(result.ObjectiveBreakdown, request.PriceUnit);
        var warnings = result.Warnings.ToArray();
        var producedVersion = request.BaseScheduleVersion + 1;

        if (outcome.PersistSchedule)
        {
            // Plan-RM-M5-01 §Fallback-Matrix „nicht-finite, schema-
            // ungültige oder constraint-verletzende Trajektorie":
            // Ergebnis verwerfen statt einer Exception aus dem Schedule-
            // Konstruktor zu blasen. `target_power_kw=NaN`/`Infinity`,
            // leere Schedule-Points, oder verletzter chronologischer
            // Vertrag mappen alle auf einen Failed-Run mit
            // `transport-internal-error`-Reason.
            if (!TryBuildSchedule(request, result, producedVersion,
                    out var schedule, out var validationDetail))
            {
                OptimizationCoreLog.LogInvalidTrajectory(_logger, validationDetail);
                var rejectedRun = CreateRun(
                    request,
                    horizonStartUtc,
                    OptimizationSolverStatus.Failed,
                    terminationCode: "invalid-trajectory",
                    terminationDetail: validationDetail,
                    elapsed: solverRuntime,
                    objectiveValue: 0.0,
                    breakdown: OptimizationObjectiveBreakdown.Empty,
                    warnings: warnings,
                    producedSchedule: null,
                    solverName: NormalizeSolverName(result.SolverName));
                return new ScheduleOptimizationResult(rejectedRun, producedSchedule: null);
            }
            var producedRef = new ScheduleReference(
                request.AssetId, request.ScheduleType, producedVersion);
            var run = CreateRun(
                request,
                horizonStartUtc,
                outcome.Status,
                terminationCode: NormalizeTerminationCode(result.TerminationCode),
                terminationDetail: NormalizeTerminationDetail(result.TerminationDetail),
                elapsed: solverRuntime,
                objectiveValue: result.ObjectiveValue,
                breakdown: breakdown,
                warnings: warnings,
                producedSchedule: producedRef,
                solverName: NormalizeSolverName(result.SolverName));
            return new ScheduleOptimizationResult(run, schedule);
        }

        var failedRun = CreateRun(
            request,
            horizonStartUtc,
            outcome.Status,
            terminationCode: NormalizeTerminationCode(result.TerminationCode),
            terminationDetail: NormalizeTerminationDetail(result.TerminationDetail),
            elapsed: solverRuntime,
            objectiveValue: 0.0,
            breakdown: OptimizationObjectiveBreakdown.Empty,
            warnings: warnings,
            producedSchedule: null,
            solverName: NormalizeSolverName(result.SolverName));
        return new ScheduleOptimizationResult(failedRun, producedSchedule: null);
    }

    private ScheduleOptimizationResult BuildFailedResult(
        ScheduleOptimizationRequest request,
        DateTimeOffset horizonStartUtc,
        OptimizationCoreOutcome outcome,
        string terminationCode,
        string? terminationDetail,
        TimeSpan elapsed)
    {
        var run = CreateRun(
            request,
            horizonStartUtc,
            outcome.Status,
            terminationCode: terminationCode,
            terminationDetail: terminationDetail,
            elapsed: elapsed,
            objectiveValue: 0.0,
            breakdown: OptimizationObjectiveBreakdown.Empty,
            warnings: Array.Empty<string>(),
            producedSchedule: null,
            solverName: SolverName);
        return new ScheduleOptimizationResult(run, producedSchedule: null);
    }

    private OptimizationRun CreateRun(
        ScheduleOptimizationRequest request,
        DateTimeOffset horizonStartUtc,
        OptimizationSolverStatus status,
        string terminationCode,
        string? terminationDetail,
        TimeSpan elapsed,
        double objectiveValue,
        OptimizationObjectiveBreakdown breakdown,
        IReadOnlyList<string> warnings,
        ScheduleReference? producedSchedule,
        string solverName)
    {
        return new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: solverName,
            status: status,
            horizonStart: horizonStartUtc,
            horizonEnd: horizonStartUtc + (request.HorizonEnd - request.HorizonStart),
            timeStep: request.TimeStep,
            objectiveValue: objectiveValue,
            objectiveBreakdown: breakdown,
            constraintViolations: Array.Empty<string>(),
            warnings: warnings,
            solverRuntime: elapsed,
            terminationCode: terminationCode,
            terminationDetail: terminationDetail,
            createdAt: _clock.UtcNow,
            inputs: request.Inputs,
            producedSchedule: producedSchedule);
    }

    // Plan-RM-M5-01 §Fallback-Matrix: Invalid-Trajectory-Detection
    // bevor wir das Domain-Schedule konstruieren (das würde sonst per
    // ArgumentException blow-up'en). Akzeptiert: nicht-leere Liste
    // von Schedule-Points mit chronologisch aufsteigenden, halb-
    // offenen Windows (Start < End, next.Start >= prev.End) und
    // finiten TargetPowerKw.
    private static bool TryBuildSchedule(
        ScheduleOptimizationRequest request,
        Grpc.V1.OptimizeResult result,
        int producedVersion,
        out Schedule? schedule,
        out string validationDetail)
    {
        schedule = null;
        if (result.SchedulePoints.Count == 0)
        {
            validationDetail = "schedule-points-empty";
            return false;
        }
        var windows = new ScheduleWindow[result.SchedulePoints.Count];
        for (var i = 0; i < result.SchedulePoints.Count; i++)
        {
            var p = result.SchedulePoints[i];
            if (!double.IsFinite(p.TargetPowerKw))
            {
                validationDetail = $"non-finite-power-at-index-{i}";
                return false;
            }
            var start = p.WindowStart.ToDateTimeOffset();
            var end = p.WindowEnd.ToDateTimeOffset();
            if (start >= end)
            {
                validationDetail = $"non-positive-window-duration-at-index-{i}";
                return false;
            }
            if (i > 0 && windows[i - 1].End > start)
            {
                validationDetail = $"overlapping-windows-at-index-{i}";
                return false;
            }
            windows[i] = new ScheduleWindow(start, end, p.TargetPowerKw);
        }
        schedule = new Schedule(
            assetId: request.AssetId,
            type: request.ScheduleType,
            marketBidArea: request.MarketBidArea,
            version: producedVersion,
            windows: windows);
        validationDetail = string.Empty;
        return true;
    }

    // Konvertiert das Proto-ObjectiveBreakdown (3 named doubles) in
    // M2-Komponenten. Sidecar-Side darf fehlen → Empty-Breakdown.
    // Unit: aus dem Request-PriceUnit; Default "EUR" wenn nicht
    // gesetzt (M2-Convention für monetäre Objectives).
    private static OptimizationObjectiveBreakdown BuildObjectiveBreakdown(
        Grpc.V1.ObjectiveBreakdown? proto, string? priceUnit)
    {
        if (proto is null) { return OptimizationObjectiveBreakdown.Empty; }
        var unit = string.IsNullOrWhiteSpace(priceUnit) ? "EUR" : priceUnit!;
        return new OptimizationObjectiveBreakdown(new[]
        {
            new OptimizationObjectiveComponent("energy_cost", proto.EnergyCost, unit),
            new OptimizationObjectiveComponent("degradation_cost", proto.DegradationCost, unit),
            new OptimizationObjectiveComponent("soc_target_penalty", proto.SocTargetPenalty, unit),
        });
    }

    private static string NormalizeTerminationCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "sidecar-no-termination-code" : code;

    private static string? NormalizeTerminationDetail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? null : detail;

    private static string NormalizeSolverName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? SolverName : name!;

    // Plan-RM-M5 §Request-Idempotenz: deterministischer Idempotency-
    // Key aus dem fachlichen Identitäts-Tupel. SHA-256-Hash auf einer
    // canonical-form-String-Repräsentation, formatiert als UUIDv5-
    // style GUID-String für DB/Wire-Kompatibilität.
    private static string ComputeRequestId(ScheduleOptimizationRequest request)
    {
        var canonical = string.Join('|',
            request.AssetId,
            request.ScheduleType.ToString(),
            request.HorizonStart.ToUniversalTime().ToString("O"),
            request.HorizonEnd.ToUniversalTime().ToString("O"),
            request.TimeStep.ToString("c"),
            request.BaseScheduleVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.MarketBidArea);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        // Erste 16 Bytes als GUID; deterministisch + idempotent über
        // identische Eingaben.
        return new Guid(hash.AsSpan(0, 16).ToArray()).ToString("D");
    }

    // Plan-RM-M5 §Atomare Finalisierung: pro request_id genau ein
    // atomarer Terminalzustand. Worker-side CAS via
    // IOptimizationIdempotencyStore.TryFinalizeAsync.
    private async Task<ScheduleOptimizationResult> FinalizeAndReturnAsync(
        string requestId,
        ScheduleOptimizationResult result,
        OptimizationTerminalState terminalState,
        string terminalReason,
        FallbackSource fallbackSource,
        FallbackReason fallbackReason,
        CancellationToken cancellationToken)
    {
        // Plan-RM-M5 §Atomare Finalisierung: der Idempotency-Eintrag
        // MUSS in einem Terminalzustand landen, selbst wenn der Caller
        // mid-call canceled hat (sonst leakt der Pending-Eintrag und
        // ein späterer Retry sieht „duplicate-pending"). CancellationToken
        // wird absichtlich NICHT durchgereicht — wir verlinken nur
        // ConfigureAwait und akzeptieren einen short blocking-Tail
        // im Cancel-Pfad für State-Integrität.
        _ = cancellationToken; // bewusst nicht weitergereicht
        var producedVersion = result.ProducedSchedule?.Version;
        await _idempotencyStore.TryFinalizeAsync(
            requestId,
            terminalState,
            terminalReason,
            runId: result.Run.RunId,
            producedVersion: producedVersion,
            committedAt: _clock.UtcNow,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
        _metrics.RecordRun(
            result.Run.AssetId,
            result.Run.Status,
            MapFallbackSource(fallbackSource),
            MapFallbackReason(fallbackReason),
            terminalState,
            result.Run.SolverRuntime);
        return result;
    }

    // Frühe-Exit-Pfad wenn TryBegin einen existierenden Eintrag
    // findet. Existing-Final → late_response_ignored; Existing-Pending
    // → duplicate-request (Worker-Side-Concurrent-Call). Beide ergeben
    // Failed-Runs ohne ProducedSchedule.
    private ScheduleOptimizationResult HandleExistingIdempotencyEntry(
        ScheduleOptimizationRequest request,
        DateTimeOffset horizonStartUtc,
        OptimizationIdempotencyEntry entry,
        DateTimeOffset startedAt)
    {
        var elapsed = _clock.UtcNow - startedAt;
        if (entry.IsFinal)
        {
            OptimizationCoreLog.LogLateResponseIgnored(
                _logger, request.AssetId, entry.TerminalState);
            var result = BuildFailedResult(
                request, horizonStartUtc,
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    FallbackSource: FallbackSource.NoActivation,
                    FallbackReason: FallbackReason.LateResponseIgnored,
                    PersistSchedule: false),
                terminationCode: "late-response-ignored",
                terminationDetail: $"existing-terminal-state={entry.TerminalState}",
                elapsed: elapsed);
            _metrics.RecordRun(
                result.Run.AssetId,
                result.Run.Status,
                MapFallbackSource(FallbackSource.NoActivation),
                MapFallbackReason(FallbackReason.LateResponseIgnored),
                OptimizationTerminalState.LateResponseIgnored,
                result.Run.SolverRuntime);
            return result;
        }
        // Existing pending → Concurrent-Caller. Fail-closed.
        OptimizationCoreLog.LogDuplicateRequest(_logger, request.AssetId, entry.RequestId);
        var duplicate = BuildFailedResult(
            request, horizonStartUtc,
            new OptimizationCoreOutcome(
                Status: OptimizationSolverStatus.Failed,
                FallbackSource: FallbackSource.NoActivation,
                FallbackReason: FallbackReason.DuplicateRequest,
                PersistSchedule: false),
            terminationCode: "duplicate-request",
            terminationDetail: $"concurrent-pending-request-id={entry.RequestId}",
            elapsed: elapsed);
        _metrics.RecordRun(
            duplicate.Run.AssetId,
            duplicate.Run.Status,
            MapFallbackSource(FallbackSource.NoActivation),
            MapFallbackReason(FallbackReason.DuplicateRequest),
            OptimizationTerminalState.FailedNoActivation,
            duplicate.Run.SolverRuntime);
        return duplicate;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _versionProbeGate.Dispose();
    }
}

// Internal-flow signal that the contract-compatibility gate failed
// pre-Optimize. Exception-for-control-flow is the pragmatic choice
// here: the catch site at the OptimizeAsync entry directly maps to a
// `ClassifyContractIncompatible`-Outcome, which is symmetric with
// the RpcException-for-transport-fault catch on the same level.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1032",
    Justification = "Internal sentinel exception with a single construction site; the standard exception ctors would only add noise.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1064",
    Justification = "Internal type by design — not part of the public API surface, callers do not need to catch it from outside the adapter.")]
internal sealed class ContractIncompatibleException : Exception
{
    public string Detail { get; }

    public ContractIncompatibleException(string detail) : base(detail)
    {
        Detail = detail;
    }
}

internal static partial class OptimizationCoreLog
{
    [LoggerMessage(EventId = 5110, Level = LogLevel.Warning,
        Message = "optimization-core contract incompatible: {Detail}")]
    public static partial void LogContractIncompatible(ILogger logger, string detail);

    [LoggerMessage(EventId = 5111, Level = LogLevel.Debug,
        Message = "optimization-core progress step={StepIndex} objective={Objective}")]
    public static partial void LogProgress(ILogger logger, int stepIndex, double objective);

    [LoggerMessage(EventId = 5112, Level = LogLevel.Warning,
        Message = "optimization-core rejected sidecar result (invalid trajectory): {Detail}")]
    public static partial void LogInvalidTrajectory(ILogger logger, string detail);

    [LoggerMessage(EventId = 5113, Level = LogLevel.Information,
        Message = "optimization-core late response ignored asset_id={AssetId} prior_terminal_state={PriorTerminalState}")]
    public static partial void LogLateResponseIgnored(
        ILogger logger,
        string assetId,
        BatteryEms.Application.Optimization.OptimizationTerminalState priorTerminalState);

    [LoggerMessage(EventId = 5114, Level = LogLevel.Warning,
        Message = "optimization-core duplicate request asset_id={AssetId} request_id={RequestId}")]
    public static partial void LogDuplicateRequest(
        ILogger logger, string assetId, string requestId);

    [LoggerMessage(EventId = 5115, Level = LogLevel.Information,
        Message = "optimization-core fallback attempt asset_id={AssetId}")]
    public static partial void LogFallbackAttempt(ILogger logger, string assetId);

    [LoggerMessage(EventId = 5116, Level = LogLevel.Warning,
        Message = "optimization-core fallback failed asset_id={AssetId}; fall through to no-valid-plan")]
    public static partial void LogFallbackFailed(
        ILogger logger, string assetId, Exception ex);

    [LoggerMessage(EventId = 5117, Level = LogLevel.Warning,
        Message = "optimization-core fallback produced no schedule asset_id={AssetId} termination={TerminationCode}")]
    public static partial void LogFallbackProducedNoSchedule(
        ILogger logger, string assetId, string terminationCode);

    [LoggerMessage(EventId = 5118, Level = LogLevel.Warning,
        Message = "optimization-core fallback rejected by plan-validator asset_id={AssetId} reason={Reason} detail={Detail}")]
    public static partial void LogFallbackRejected(
        ILogger logger,
        string assetId,
        BatteryEms.Application.Optimization.FallbackReason reason,
        string detail);

    [LoggerMessage(EventId = 5119, Level = LogLevel.Information,
        Message = "optimization-core fallback committed asset_id={AssetId} solver={SolverName}")]
    public static partial void LogFallbackCommitted(
        ILogger logger, string assetId, string solverName);
}
