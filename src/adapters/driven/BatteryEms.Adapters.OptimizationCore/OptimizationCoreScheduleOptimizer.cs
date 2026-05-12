using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
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
internal sealed class OptimizationCoreScheduleOptimizer : IScheduleOptimizer, IDisposable
{
    private readonly OptimizationCoreClient _client;
    private readonly OptimizationCoreOptions _options;
    private readonly IOptimizationIdempotencyStore _idempotencyStore;
    private readonly IClock _clock;
    private readonly ILogger<OptimizationCoreScheduleOptimizer> _logger;
    private readonly IOptimizationCoreMetrics _metrics;
    private readonly OptimizationCoreResultFactory _resultFactory;
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
        _resultFactory = new OptimizationCoreResultFactory(clock, logger);
        _fallbackOptimizer = fallbackOptimizer;
        _planValidator = planValidator;
    }

    private readonly record struct OptimizationRequestContext(
        string RequestId,
        DateTimeOffset HorizonStartUtc,
        DateTimeOffset StartedAt);

    public async Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var (context, existingResult) = await BeginRequestAsync(
            request, cancellationToken).ConfigureAwait(false);
        if (existingResult is not null)
        {
            return existingResult;
        }

        try
        {
            await PrepareSidecarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ContractIncompatibleException ex)
        {
            return await HandleContractIncompatibleAsync(
                request, context, ex, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return await HandleTransportFailureAsync(
                request, context, ex, cancellationToken).ConfigureAwait(false);
        }

        return await OptimizeWithConnectedSidecarAsync(
            request, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(OptimizationRequestContext Context, ScheduleOptimizationResult? ExistingResult)>
        BeginRequestAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
    {
        var horizonStartUtc = request.HorizonStart.ToUniversalTime();
        var startedAt = _clock.UtcNow;
        var requestId = OptimizationCoreRequestIdentity.ComputeRequestId(request);
        var beginResult = await _idempotencyStore.TryBeginAsync(
            requestId, startedAt, cancellationToken).ConfigureAwait(false);
        var context = new OptimizationRequestContext(
            requestId, horizonStartUtc, startedAt);

        if (!beginResult.IsNewlyCreated)
        {
            var existing = HandleExistingIdempotencyEntry(
                request, horizonStartUtc, beginResult.Entry, startedAt);
            return (context, existing);
        }

        return (context, null);
    }

    private async Task PrepareSidecarAsync(CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await EnsureContractCompatibleAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScheduleOptimizationResult> HandleContractIncompatibleAsync(
        ScheduleOptimizationRequest request,
        OptimizationRequestContext context,
        ContractIncompatibleException exception,
        CancellationToken cancellationToken)
    {
        _metrics.RecordSidecarHealth("contract_incompatible");
        OptimizationCoreLog.LogContractIncompatible(_logger, exception.Detail);
        var contractFailed = _resultFactory.BuildFailedResult(
            request,
            context.HorizonStartUtc,
            OptimizationCoreStatusMapper.ClassifyContractIncompatible(),
            terminationCode: "contract-incompatible",
            terminationDetail: exception.Detail,
            elapsed: _clock.UtcNow - context.StartedAt);
        return await FinalizeAndReturnAsync(
            context.RequestId, contractFailed,
            OptimizationTerminalState.FailedNoActivation,
            "contract-incompatible",
            FallbackSource.NoActivation,
            FallbackReason.ContractIncompatible,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScheduleOptimizationResult> OptimizeWithConnectedSidecarAsync(
        ScheduleOptimizationRequest request,
        OptimizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var protoRequest = OptimizationCoreProtoMapper.BuildRequest(request);
        var deadline = DateTime.UtcNow + _options.RequestDeadline;

        try
        {
            using var call = _client.Client.Optimize(
                protoRequest, deadline: deadline, cancellationToken: cancellationToken);
            var final = await ReadFinalOptimizeResultAsync(
                call.ResponseStream, cancellationToken).ConfigureAwait(false);

            if (final is null)
            {
                return await HandleMissingFinalResultAsync(
                    request, context, cancellationToken).ConfigureAwait(false);
            }

            return await HandleFinalResultAsync(
                request, context, final, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return await HandleTransportFailureAsync(
                request, context, ex, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await HandleCancelledOptimizeAsync(
                request, context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Grpc.V1.OptimizeResult?> ReadFinalOptimizeResultAsync(
        IAsyncStreamReader<Grpc.V1.OptimizeUpdate> responseStream,
        CancellationToken cancellationToken)
    {
        Grpc.V1.OptimizeResult? final = null;
        await foreach (var update in responseStream
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
        return final;
    }

    private async Task<ScheduleOptimizationResult> HandleMissingFinalResultAsync(
        ScheduleOptimizationRequest request,
        OptimizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var fallbackOutcome = await TryRunFallbackAsync(
            request, context.HorizonStartUtc, cancellationToken).ConfigureAwait(false);
        if (fallbackOutcome is not null)
        {
            return await FinalizeAndReturnAsync(
                context.RequestId, fallbackOutcome,
                OptimizationTerminalState.FallbackCommitted,
                "local-optimizer-fallback-committed",
                FallbackSource.LocalOptimizer,
                FallbackReason.TransportInternalError,
                cancellationToken)
                .ConfigureAwait(false);
        }

        var streamFailed = _resultFactory.BuildFailedResult(
            request,
            context.HorizonStartUtc,
            new OptimizationCoreOutcome(
                Status: OptimizationSolverStatus.Failed,
                FallbackSource: FallbackSource.FromMatrix,
                FallbackReason: FallbackReason.TransportInternalError,
                PersistSchedule: false),
            terminationCode: "stream-closed-without-result",
            terminationDetail: null,
            elapsed: _clock.UtcNow - context.StartedAt);
        return await FinalizeAndReturnAsync(
            context.RequestId, streamFailed,
            OptimizationTerminalState.FailedNoActivation,
            "transport-internal-error",
            FallbackSource.NoActivation,
            FallbackReason.TransportInternalError,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScheduleOptimizationResult> HandleFinalResultAsync(
        ScheduleOptimizationRequest request,
        OptimizationRequestContext context,
        Grpc.V1.OptimizeResult final,
        CancellationToken cancellationToken)
    {
        var resultOutcome = OptimizationCoreStatusMapper.ClassifyResult(
            final.SolverStatus, final.HasUsableSolution);
        var built = _resultFactory.BuildResult(
            request, context.HorizonStartUtc, final, resultOutcome,
            elapsed: _clock.UtcNow - context.StartedAt);
        var (state, reason) = built.ProducedSchedule is not null
            ? (OptimizationTerminalState.SidecarCommitted, "sidecar-committed")
            : (OptimizationTerminalState.FailedNoActivation,
               OptimizationCoreTerminalTaxonomy.MapTerminalReason(resultOutcome.FallbackReason));
        var fallbackSource = built.ProducedSchedule is not null
            ? FallbackSource.SidecarResult
            : OptimizationCoreTerminalTaxonomy.ResolveFallbackSource(resultOutcome.FallbackSource, state);
        var fallbackReason = built.ProducedSchedule is not null
            ? FallbackReason.None
            : OptimizationCoreTerminalTaxonomy.ResolveFallbackReasonForBuiltResult(resultOutcome, built);
        return await FinalizeAndReturnAsync(
            context.RequestId, built, state, reason, fallbackSource, fallbackReason, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScheduleOptimizationResult> HandleTransportFailureAsync(
        ScheduleOptimizationRequest request,
        OptimizationRequestContext context,
        RpcException exception,
        CancellationToken cancellationToken)
    {
        _metrics.RecordSidecarHealth("unavailable");
        var outcome = OptimizationCoreStatusMapper.ClassifyTransport(exception.StatusCode);
        var fallbackOutcome = await TryRunFallbackAsync(
            request, context.HorizonStartUtc, cancellationToken).ConfigureAwait(false);
        if (fallbackOutcome is not null)
        {
            return await FinalizeAndReturnAsync(
                context.RequestId, fallbackOutcome,
                OptimizationTerminalState.FallbackCommitted,
                "local-optimizer-fallback-committed",
                FallbackSource.LocalOptimizer,
                outcome.FallbackReason,
                cancellationToken)
                .ConfigureAwait(false);
        }

        var transportFailed = _resultFactory.BuildFailedResult(
            request,
            context.HorizonStartUtc,
            outcome,
            terminationCode: exception.StatusCode.ToString(),
            terminationDetail: OptimizationCoreResultFactory.NormalizeTerminationDetail(exception.Status.Detail),
            elapsed: _clock.UtcNow - context.StartedAt);
        return await FinalizeAndReturnAsync(
            context.RequestId, transportFailed,
            OptimizationTerminalState.FailedNoActivation,
            OptimizationCoreTerminalTaxonomy.MapTerminalReason(outcome.FallbackReason),
            OptimizationCoreTerminalTaxonomy.ResolveFallbackSource(
                outcome.FallbackSource, OptimizationTerminalState.FailedNoActivation),
            outcome.FallbackReason,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScheduleOptimizationResult> HandleCancelledOptimizeAsync(
        ScheduleOptimizationRequest request,
        OptimizationRequestContext context,
        CancellationToken cancellationToken)
    {
        var cancelledOutcome = new OptimizationCoreOutcome(
            Status: OptimizationSolverStatus.Failed,
            FallbackSource: FallbackSource.FromMatrix,
            FallbackReason: FallbackReason.TransportCancelled,
            PersistSchedule: false);
        var cancelled = _resultFactory.BuildFailedResult(
            request, context.HorizonStartUtc, cancelledOutcome,
            terminationCode: "Cancelled",
            terminationDetail: "caller-cancelled-mid-optimize",
            elapsed: _clock.UtcNow - context.StartedAt);
        return await FinalizeAndReturnAsync(
            context.RequestId, cancelled,
            OptimizationTerminalState.Cancelled,
            "transport-cancelled",
            FallbackSource.NoActivation,
            FallbackReason.TransportCancelled,
            cancellationToken)
            .ConfigureAwait(false);
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
            OptimizationCoreTerminalTaxonomy.MapFallbackSource(fallbackSource),
            OptimizationCoreTerminalTaxonomy.MapFallbackReason(fallbackReason),
            terminalState,
            result.Run.SolverRuntime);
        OptimizationCoreLog.LogRunFinalized(
            _logger,
            result.Run.AssetId,
            result.Run.RunId,
            requestId,
            terminalState,
            terminalReason);
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
            var result = _resultFactory.BuildFailedResult(
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
                OptimizationCoreTerminalTaxonomy.MapFallbackSource(FallbackSource.NoActivation),
                OptimizationCoreTerminalTaxonomy.MapFallbackReason(FallbackReason.LateResponseIgnored),
                OptimizationTerminalState.LateResponseIgnored,
                result.Run.SolverRuntime);
            return result;
        }
        // Existing pending → Concurrent-Caller. Fail-closed.
        OptimizationCoreLog.LogDuplicateRequest(_logger, request.AssetId, entry.RequestId);
        var duplicate = _resultFactory.BuildFailedResult(
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
            OptimizationCoreTerminalTaxonomy.MapFallbackSource(FallbackSource.NoActivation),
            OptimizationCoreTerminalTaxonomy.MapFallbackReason(FallbackReason.DuplicateRequest),
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

    [LoggerMessage(EventId = 5120, Level = LogLevel.Information,
        Message = "optimization-core run finalized asset_id={AssetId} run_id={RunId} request_id={RequestId} terminal_state={TerminalState} terminal_reason={TerminalReason}")]
    public static partial void LogRunFinalized(
        ILogger logger,
        string assetId,
        Guid runId,
        string requestId,
        BatteryEms.Application.Optimization.OptimizationTerminalState terminalState,
        string terminalReason);
}
