using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Google.OrTools.LinearSolver;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.Optimization.OrTools;

// RM-M2-OP-05: production schedule optimiser backed by OR-Tools' GLOP
// linear solver. The model in §Arbeitsmodell of plan-RM-M2-optimization:
// per time-step charge / discharge / SOC variables, SOC dynamics with
// efficiency, fixed initial SOC, free terminal SOC, energy-cost
// objective (PriceUnit "EUR/MWh"), no MILP because LP-relaxation under
// η<1 already discourages simultaneous charge + discharge.
//
// Schedule identity (MarketBidArea + version) is supplied on the
// request by the use case (RM-M2-OP-05 review #1/#3) — the optimiser
// no longer reaches into IScheduleRepository to derive it, so the
// read-optimise-write race against parallel calls disappears (the use
// case serialises per (asset, type)).
public sealed partial class OrToolsScheduleOptimizer : IScheduleOptimizer
{
    private const string SolverName = "or-tools-glop";
    private const string SupportedPriceUnit = "EUR/MWh";

    // Solutions whose absolute objective value sits below this threshold
    // are snapped to zero before being recorded in the run breakdown so
    // a trivial optimum (flat prices, no profitable arbitrage) does not
    // surface floating-point noise like -1.4e-12 EUR (review #18).
    private const double ObjectiveZeroEpsilon = 1e-9;

    private readonly ScheduleSolverOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<OrToolsScheduleOptimizer> _logger;

    // Test seam: lets a test substitute the backend's Solve() return
    // value so the non-solution path (Infeasible / Unbounded / Failed)
    // can be exercised without forcing a contrived LP that GLOP rejects
    // (review #7). null in production = use the real backend status.
    private readonly Func<Solver.ResultStatus, Solver.ResultStatus>? _resultStatusOverride;

    public OrToolsScheduleOptimizer(
        ScheduleSolverOptions options,
        IClock clock,
        ILogger<OrToolsScheduleOptimizer> logger)
        : this(options, clock, logger, resultStatusOverride: null)
    {
    }

    internal OrToolsScheduleOptimizer(
        ScheduleSolverOptions options,
        IClock clock,
        ILogger<OrToolsScheduleOptimizer> logger,
        Func<Solver.ResultStatus, Solver.ResultStatus>? resultStatusOverride)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.EnsureValid();
        _clock = clock;
        _logger = logger;
        _resultStatusOverride = resultStatusOverride;
    }

    public Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Pre-flight checks that don't need a solver instance — keep the
        // failure path fast and fully deterministic.
        if (request.PricesPerStep is null)
        {
            return Task.FromResult(BuildFailedResult(request,
                terminationCode: "missing-prices",
                terminationDetail: null,
                warning: "PricesPerStep is required for energy-cost optimisation."));
        }
        if (!string.Equals(request.PriceUnit, SupportedPriceUnit, StringComparison.Ordinal))
        {
            return Task.FromResult(BuildFailedResult(request,
                terminationCode: "unsupported-price-unit",
                terminationDetail: request.PriceUnit,
                warning: $"OR-Tools schedule optimiser only accepts PriceUnit '{SupportedPriceUnit}'."));
        }

        return Task.FromResult(Solve(request, cancellationToken));
    }

    private ScheduleOptimizationResult Solve(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        var asset = request.Asset;
        var n = request.StepCount;
        var dtHours = request.TimeStep.TotalHours;
        var capacityKwh = asset.CapacityKwh;
        var socMinKwh = asset.MinSocPercent / 100.0 * capacityKwh;
        var socMaxKwh = asset.MaxSocPercent / 100.0 * capacityKwh;

        var initialSocPercent = _options.InitialSocPercent
            ?? (asset.MinSocPercent + asset.MaxSocPercent) / 2.0;
        if (initialSocPercent < asset.MinSocPercent || initialSocPercent > asset.MaxSocPercent)
        {
            return BuildFailedResult(request,
                terminationCode: "initial-soc-out-of-bounds",
                terminationDetail: System.FormattableString.Invariant(
                    $"{initialSocPercent} not in [{asset.MinSocPercent}, {asset.MaxSocPercent}]"),
                warning: $"InitialSocPercent {initialSocPercent} is outside the asset's SOC band " +
                    $"[{asset.MinSocPercent}, {asset.MaxSocPercent}].");
        }
        var initialSocKwh = initialSocPercent / 100.0 * capacityKwh;

        // GLOP ships with the Google.OrTools NuGet, so CreateSolver is
        // guaranteed to succeed; if the native bindings ever go missing
        // the call throws DllNotFoundException, which the use case treats
        // as a solver crash (no run persisted).
        var solver = Solver.CreateSolver("GLOP");

        try
        {
            if (_options.TimeLimit is { } limit)
            {
                solver.SetTimeLimit((long)limit.TotalMilliseconds);
            }

            var charge = new Variable[n];
            var discharge = new Variable[n];
            var soc = new Variable[n + 1];
            for (var t = 0; t < n; t++)
            {
                charge[t] = solver.MakeNumVar(0, asset.MaxChargePowerKw, $"p_charge_{t}");
                discharge[t] = solver.MakeNumVar(0, asset.MaxDischargePowerKw, $"p_discharge_{t}");
            }
            for (var t = 0; t <= n; t++)
            {
                soc[t] = solver.MakeNumVar(socMinKwh, socMaxKwh, $"soc_{t}");
            }

            // Initial SOC pinned (LP equality is a [c, c] range constraint).
            var initial = solver.MakeConstraint(initialSocKwh, initialSocKwh, "initial_soc");
            initial.SetCoefficient(soc[0], 1.0);

            // SOC dynamics:
            //   soc[t+1] = soc[t] + ηC * p_charge[t] * Δt − p_discharge[t] / ηD * Δt
            // → soc[t+1] − soc[t] − ηC * Δt * p_charge[t] + Δt/ηD * p_discharge[t] = 0
            for (var t = 0; t < n; t++)
            {
                var dyn = solver.MakeConstraint(0, 0, $"soc_dyn_{t}");
                dyn.SetCoefficient(soc[t + 1], 1.0);
                dyn.SetCoefficient(soc[t], -1.0);
                dyn.SetCoefficient(charge[t], -asset.ChargeEfficiency * dtHours);
                dyn.SetCoefficient(discharge[t], dtHours / asset.DischargeEfficiency);
            }

            // Objective: minimise day-ahead energy cost (EUR).
            //   cost_t = price[t] (EUR/MWh) * (p_charge − p_discharge) (kW) * Δt (h) / 1000
            // Charging draws from the grid (cost), discharging exports (revenue, negative cost).
            var objective = solver.Objective();
            for (var t = 0; t < n; t++)
            {
                var coef = request.PricesPerStep![t] * dtHours / 1000.0;
                objective.SetCoefficient(charge[t], coef);
                objective.SetCoefficient(discharge[t], -coef);
            }
            objective.SetMinimization();

            var rawBackendStatus = solver.Solve();
            var backendStatus = _resultStatusOverride is null
                ? rawBackendStatus
                : _resultStatusOverride(rawBackendStatus);
            var elapsed = TimeSpan.FromMilliseconds(solver.WallTime());
            // Post-solve cancellation — GLOP itself is uncancellable
            // mid-solve, but the build path that follows can take long
            // enough for a cooperative caller to give up (review #9).
            cancellationToken.ThrowIfCancellationRequested();
            var (mappedStatus, terminationCode, terminationDetail) = OrToolsResultMapper.Map(
                backendStatus, elapsed, _options.TimeLimit);

            Log.SolveCompleted(_logger, request.AssetId, mappedStatus, elapsed.TotalMilliseconds, n);

            if (mappedStatus is OptimizationSolverStatus.Optimal or OptimizationSolverStatus.Feasible)
            {
                var rawObjective = objective.Value();
                // Snap floating-point noise around a trivial optimum to
                // exact zero before persisting (review #18) — keeps a
                // flat-price idle run from surfacing a -1.4e-12 EUR
                // breakdown that confuses dashboards.
                var snappedObjective = Math.Abs(rawObjective) < ObjectiveZeroEpsilon ? 0.0 : rawObjective;
                return BuildSolutionResult(
                    request, mappedStatus, terminationCode, terminationDetail,
                    charge, discharge, snappedObjective, elapsed);
            }

            return BuildNonSolutionResult(request, mappedStatus, terminationCode, terminationDetail, elapsed);
        }
        finally
        {
            solver.Dispose();
        }
    }

    private ScheduleOptimizationResult BuildSolutionResult(
        ScheduleOptimizationRequest request,
        OptimizationSolverStatus status,
        string terminationCode,
        string? terminationDetail,
        Variable[] charge,
        Variable[] discharge,
        double objectiveValue,
        TimeSpan elapsed)
    {
        var n = request.StepCount;
        // Defensive normalisation to UTC: Schedule's downstream consumers
        // (loader, persistence, IScheduleTracker) assume Offset == Zero
        // (Schedule.cs:13). The request-level constructor doesn't enforce
        // the offset, so do it once here to keep windows in canonical form
        // (review #4); CreateRun applies the same normalisation to the run
        // record so audit log and produced schedule never disagree (#N1).
        var horizonStartUtc = request.HorizonStart.ToUniversalTime();
        var windows = new ScheduleWindow[n];
        for (var t = 0; t < n; t++)
        {
            // Domain convention: discharge positive, charge negative.
            var targetKw = discharge[t].SolutionValue() - charge[t].SolutionValue();
            var start = horizonStartUtc + TimeSpan.FromTicks(request.TimeStep.Ticks * t);
            var end = horizonStartUtc + TimeSpan.FromTicks(request.TimeStep.Ticks * (t + 1));
            windows[t] = new ScheduleWindow(start, end, targetKw);
        }

        var schedule = new Schedule(
            request.AssetId,
            request.ScheduleType,
            request.MarketBidArea,
            request.BaseScheduleVersion + 1,
            windows);

        var producedReference = new ScheduleReference(
            schedule.AssetId, schedule.Type, schedule.Version);

        var breakdown = new OptimizationObjectiveBreakdown(new[]
        {
            new OptimizationObjectiveComponent("energy_cost", objectiveValue, "EUR"),
        });

        var run = CreateRun(
            request, horizonStartUtc, status, terminationCode, terminationDetail, elapsed,
            objectiveValue, breakdown,
            warnings: Array.Empty<string>(),
            producedSchedule: producedReference);
        return new ScheduleOptimizationResult(run, schedule);
    }

    // Plan §Resultatvertrag mandates this output shape for non-solution
    // statuses (Infeasible / Unbounded / Failed / TimeLimit). The path is
    // exercised end-to-end by the test that injects a custom result-status
    // override (review #7).
    private ScheduleOptimizationResult BuildNonSolutionResult(
        ScheduleOptimizationRequest request,
        OptimizationSolverStatus status,
        string terminationCode,
        string? terminationDetail,
        TimeSpan elapsed)
    {
        var run = CreateRun(
            request, request.HorizonStart.ToUniversalTime(),
            status, terminationCode, terminationDetail, elapsed,
            objectiveValue: 0,
            breakdown: OptimizationObjectiveBreakdown.Empty,
            warnings: Array.Empty<string>(),
            producedSchedule: null);
        return new ScheduleOptimizationResult(run, producedSchedule: null);
    }

    private ScheduleOptimizationResult BuildFailedResult(
        ScheduleOptimizationRequest request,
        string terminationCode,
        string? terminationDetail,
        string warning)
    {
        Log.PreflightFailed(_logger, request.AssetId, terminationCode);
        var run = CreateRun(
            request, request.HorizonStart.ToUniversalTime(),
            status: OptimizationSolverStatus.Failed,
            terminationCode: terminationCode,
            terminationDetail: terminationDetail,
            elapsed: TimeSpan.Zero,
            objectiveValue: 0,
            breakdown: OptimizationObjectiveBreakdown.Empty,
            warnings: new[] { warning },
            producedSchedule: null);
        return new ScheduleOptimizationResult(run, producedSchedule: null);
    }

    // Single source of truth for OptimizationRun construction (review #17):
    // every adapter-side run record flows through here, so a new field on
    // OptimizationRun changes one site instead of three. Callers pre-
    // compute the UTC-normalised horizon start (review #4 / N1 / 3rd-pass
    // #11) so BuildSolutionResult's window-construction loop and this
    // run-record builder agree on a single value without recomputing
    // ToUniversalTime() twice.
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
        ScheduleReference? producedSchedule)
    {
        return new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: SolverName,
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

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(EventId = 2101, Level = LogLevel.Information,
            Message = "OR-Tools schedule optimisation finished asset_id={asset_id} status={status} runtime_ms={runtime_ms} steps={steps}")]
        public static partial void SolveCompleted(
            ILogger logger,
            string asset_id,
            OptimizationSolverStatus status,
            double runtime_ms,
            int steps);

        [LoggerMessage(EventId = 2102, Level = LogLevel.Warning,
            Message = "OR-Tools schedule optimisation pre-flight failed asset_id={asset_id} reason={reason}")]
        public static partial void PreflightFailed(
            ILogger logger,
            string asset_id,
            string reason);
    }
}
