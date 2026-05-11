using System.Security.Cryptography;
using System.Text;
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
internal sealed class OptimizationCoreScheduleOptimizer : IScheduleOptimizer, IDisposable
{
    private const string SolverName = "optimization-core";

    private readonly OptimizationCoreClient _client;
    private readonly OptimizationCoreOptions _options;
    private readonly IOptimizationIdempotencyStore _idempotencyStore;
    private readonly IClock _clock;
    private readonly ILogger<OptimizationCoreScheduleOptimizer> _logger;
    private readonly SemaphoreSlim _versionProbeGate = new(1, 1);
    private bool _versionProbeDone;
    private bool _disposed;

    public OptimizationCoreScheduleOptimizer(
        OptimizationCoreClient client,
        OptimizationCoreOptions options,
        IOptimizationIdempotencyStore idempotencyStore,
        IClock clock,
        ILogger<OptimizationCoreScheduleOptimizer> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(idempotencyStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid(logger);

        _client = client;
        _options = options;
        _idempotencyStore = idempotencyStore;
        _clock = clock;
        _logger = logger;
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
                "contract-incompatible", cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            var outcome = OptimizationCoreStatusMapper.ClassifyTransport(ex.StatusCode);
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
                MapTerminalReason(outcome.FallbackReason), cancellationToken)
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
                    "transport-internal-error", cancellationToken).ConfigureAwait(false);
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
            return await FinalizeAndReturnAsync(
                requestId, built, state, reason, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            var outcome = OptimizationCoreStatusMapper.ClassifyTransport(ex.StatusCode);
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
                MapTerminalReason(outcome.FallbackReason), cancellationToken)
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
                "transport-cancelled", cancellationToken)
                .ConfigureAwait(false);
        }
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
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    $"sidecar-not-serving: status={health.Status}"));
            }

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
            return BuildFailedResult(
                request, horizonStartUtc,
                new OptimizationCoreOutcome(
                    Status: OptimizationSolverStatus.Failed,
                    FallbackSource: FallbackSource.NoActivation,
                    FallbackReason: FallbackReason.LateResponseIgnored,
                    PersistSchedule: false),
                terminationCode: "late-response-ignored",
                terminationDetail: $"existing-terminal-state={entry.TerminalState}",
                elapsed: elapsed);
        }
        // Existing pending → Concurrent-Caller. Fail-closed.
        OptimizationCoreLog.LogDuplicateRequest(_logger, request.AssetId, entry.RequestId);
        return BuildFailedResult(
            request, horizonStartUtc,
            new OptimizationCoreOutcome(
                Status: OptimizationSolverStatus.Failed,
                FallbackSource: FallbackSource.NoActivation,
                FallbackReason: FallbackReason.DuplicateRequest,
                PersistSchedule: false),
            terminationCode: "duplicate-request",
            terminationDetail: $"concurrent-pending-request-id={entry.RequestId}",
            elapsed: elapsed);
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
}
