using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OptimizationCore;

internal sealed class OptimizationCoreResultFactory
{
    private const string SolverName = "optimization-core";

    private readonly IClock _clock;
    private readonly ILogger _logger;

    public OptimizationCoreResultFactory(IClock clock, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _clock = clock;
        _logger = logger;
    }

    public ScheduleOptimizationResult BuildResult(
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

    public ScheduleOptimizationResult BuildFailedResult(
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

    public static string NormalizeTerminationCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "sidecar-no-termination-code" : code;

    public static string? NormalizeTerminationDetail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? null : detail;

    private static string NormalizeSolverName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? SolverName : name!;
}
