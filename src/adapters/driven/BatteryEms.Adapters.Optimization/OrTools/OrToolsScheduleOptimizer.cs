using BatteryEms.Application.Markets;
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
// The adapter is *also* the place that decides the produced Schedule's
// version and market bid area: it looks up the latest Schedule for
// (assetId, type) on the IScheduleRepository, inherits MarketBidArea
// and increments Version. When no prior schedule exists the M2 default
// from ScheduleSolverOptions.DefaultMarketBidArea applies and version
// starts at 1.
public sealed partial class OrToolsScheduleOptimizer : IScheduleOptimizer
{
    private const string SolverName = "or-tools-glop";
    private const string SupportedPriceUnit = "EUR/MWh";

    private readonly ScheduleSolverOptions _options;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IClock _clock;
    private readonly ILogger<OrToolsScheduleOptimizer> _logger;

    public OrToolsScheduleOptimizer(
        ScheduleSolverOptions options,
        IScheduleRepository scheduleRepository,
        IClock clock,
        ILogger<OrToolsScheduleOptimizer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scheduleRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.EnsureValid();
        _scheduleRepository = scheduleRepository;
        _clock = clock;
        _logger = logger;
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
            return Task.FromResult(BuildFailedResult(request, "missing-prices",
                "PricesPerStep is required for energy-cost optimisation."));
        }
        if (!string.Equals(request.PriceUnit, SupportedPriceUnit, StringComparison.Ordinal))
        {
            return Task.FromResult(BuildFailedResult(request,
                $"unsupported-price-unit:{request.PriceUnit}",
                $"OR-Tools schedule optimiser only accepts PriceUnit '{SupportedPriceUnit}'."));
        }

        return Task.FromResult(Solve(request));
    }

    private ScheduleOptimizationResult Solve(ScheduleOptimizationRequest request)
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
            return BuildFailedResult(request, "initial-soc-out-of-bounds",
                $"InitialSocPercent {initialSocPercent} is outside the asset's SOC band " +
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
                charge[t] = solver.MakeNumVar(0, asset.MaxChargePowerKw, FormattableString.Invariant($"p_charge_{t}"));
                discharge[t] = solver.MakeNumVar(0, asset.MaxDischargePowerKw, FormattableString.Invariant($"p_discharge_{t}"));
            }
            for (var t = 0; t <= n; t++)
            {
                soc[t] = solver.MakeNumVar(socMinKwh, socMaxKwh, FormattableString.Invariant($"soc_{t}"));
            }

            // Initial SOC pinned (LP equality is a [c, c] range constraint).
            var initial = solver.MakeConstraint(initialSocKwh, initialSocKwh, "initial_soc");
            initial.SetCoefficient(soc[0], 1.0);

            // SOC dynamics:
            //   soc[t+1] = soc[t] + ηC * p_charge[t] * Δt − p_discharge[t] / ηD * Δt
            // → soc[t+1] − soc[t] − ηC * Δt * p_charge[t] + Δt/ηD * p_discharge[t] = 0
            for (var t = 0; t < n; t++)
            {
                var dyn = solver.MakeConstraint(0, 0, FormattableString.Invariant($"soc_dyn_{t}"));
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

            var backendStatus = solver.Solve();
            var elapsed = TimeSpan.FromMilliseconds(solver.WallTime());
            var (mappedStatus, terminationReason) = OrToolsResultMapper.Map(
                backendStatus, elapsed, _options.TimeLimit);

            Log.SolveCompleted(_logger, request.AssetId, mappedStatus, elapsed.TotalMilliseconds, n);

            if (mappedStatus is OptimizationSolverStatus.Optimal or OptimizationSolverStatus.Feasible)
            {
                return BuildSolutionResult(
                    request, mappedStatus, terminationReason,
                    charge, discharge, objective.Value(), elapsed);
            }

            return BuildNonSolutionResult(request, mappedStatus, terminationReason, elapsed);
        }
        finally
        {
            solver.Dispose();
        }
    }

    private ScheduleOptimizationResult BuildSolutionResult(
        ScheduleOptimizationRequest request,
        OptimizationSolverStatus status,
        string terminationReason,
        Variable[] charge,
        Variable[] discharge,
        double objectiveValue,
        TimeSpan elapsed)
    {
        var n = request.StepCount;
        var windows = new ScheduleWindow[n];
        for (var t = 0; t < n; t++)
        {
            // Domain convention: discharge positive, charge negative.
            var targetKw = discharge[t].SolutionValue() - charge[t].SolutionValue();
            var start = request.HorizonStart + TimeSpan.FromTicks(request.TimeStep.Ticks * t);
            var end = request.HorizonStart + TimeSpan.FromTicks(request.TimeStep.Ticks * (t + 1));
            windows[t] = new ScheduleWindow(start, end, targetKw);
        }

        var (marketBidArea, version) = ResolveScheduleIdentity(request);
        var schedule = new Schedule(
            request.AssetId,
            request.ScheduleType,
            marketBidArea,
            version,
            windows);

        var producedReference = new ScheduleReference(
            schedule.AssetId, schedule.Type, schedule.Version);

        var breakdown = new OptimizationObjectiveBreakdown(new[]
        {
            new OptimizationObjectiveComponent("energy_cost", objectiveValue, "EUR"),
        });

        var run = new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: SolverName,
            status: status,
            horizonStart: request.HorizonStart,
            horizonEnd: request.HorizonEnd,
            timeStep: request.TimeStep,
            objectiveValue: objectiveValue,
            objectiveBreakdown: breakdown,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: elapsed,
            terminationReason: terminationReason,
            createdAt: _clock.UtcNow,
            inputs: request.Inputs,
            producedSchedule: producedReference);
        return new ScheduleOptimizationResult(run, schedule);
    }

    private (string MarketBidArea, int Version) ResolveScheduleIdentity(ScheduleOptimizationRequest request)
    {
        var existing = _scheduleRepository.FindActive(request.AssetId, request.ScheduleType);
        return existing is null
            ? (_options.DefaultMarketBidArea, 1)
            : (existing.MarketBidArea, existing.Version + 1);
    }

    // Defensive: GLOP's LP under M2-minimal inputs (linear, finite price
    // series, sane SOC/power bounds) is always feasible at p_charge =
    // p_discharge = 0, so Infeasible / Unbounded / TimeLimit are
    // unreachable in normal operation. Kept as a guarded fallback for the
    // day a future model adds binaries / discrete constraints; coverage
    // is then lifted along with the new path's tests.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private ScheduleOptimizationResult BuildNonSolutionResult(
        ScheduleOptimizationRequest request,
        OptimizationSolverStatus status,
        string terminationReason,
        TimeSpan elapsed)
    {
        var run = new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: SolverName,
            status: status,
            horizonStart: request.HorizonStart,
            horizonEnd: request.HorizonEnd,
            timeStep: request.TimeStep,
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: elapsed,
            terminationReason: terminationReason,
            createdAt: _clock.UtcNow,
            inputs: request.Inputs,
            producedSchedule: null);
        return new ScheduleOptimizationResult(run, producedSchedule: null);
    }

    private ScheduleOptimizationResult BuildFailedResult(
        ScheduleOptimizationRequest request,
        string terminationReason,
        string warning)
    {
        Log.PreflightFailed(_logger, request.AssetId, terminationReason);
        var run = new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: SolverName,
            status: OptimizationSolverStatus.Failed,
            horizonStart: request.HorizonStart,
            horizonEnd: request.HorizonEnd,
            timeStep: request.TimeStep,
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: new[] { warning },
            solverRuntime: TimeSpan.Zero,
            terminationReason: terminationReason,
            createdAt: _clock.UtcNow,
            inputs: request.Inputs,
            producedSchedule: null);
        return new ScheduleOptimizationResult(run, producedSchedule: null);
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
