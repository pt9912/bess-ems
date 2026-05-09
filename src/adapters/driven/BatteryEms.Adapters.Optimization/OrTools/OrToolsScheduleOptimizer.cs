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

        // RM-M4-02 LH-MKT-004: deduct held reserve bands from the
        // per-step charge/discharge caps. Symmetric (FCR) reserves
        // withhold magnitude in BOTH directions; Up reserves withhold
        // only on the discharge side; Down reserves withhold only on
        // the charge side. If reserves over-commit a step beyond the
        // asset's nameplate the run terminates with a specific code
        // before any LP variables are built (operator-actionable
        // signal vs. an opaque LP-infeasible).
        var (effChargeMax, effDischargeMax) = ComputeReserveCaps(request, n);
        for (var t = 0; t < n; t++)
        {
            if (effChargeMax[t] < 0 || effDischargeMax[t] < 0)
            {
                return BuildFailedResult(request,
                    terminationCode: "reserve-exceeds-capacity",
                    terminationDetail: System.FormattableString.Invariant(
                        $"step {t}: charge_cap={effChargeMax[t]:F3}kW, discharge_cap={effDischargeMax[t]:F3}kW"),
                    warning: $"Held reserve at step {t} exceeds asset capacity " +
                        $"(charge_cap={effChargeMax[t]:F3} kW, discharge_cap={effDischargeMax[t]:F3} kW).");
            }
        }

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
                charge[t] = solver.MakeNumVar(0, effChargeMax[t], $"p_charge_{t}");
                discharge[t] = solver.MakeNumVar(0, effDischargeMax[t], $"p_discharge_{t}");
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

            // Optional SOC-target slack variables (RM-M2-04). Created
            // here when the option is set so the LP holds them; their
            // objective coefficients are added below alongside the
            // other components.
            var (slackBelow, slackAbove) = BuildSocTargetSlacks(solver, soc, n, capacityKwh);

            // Objective: minimise total cost across all configured
            // components (LH-OPT-004 "konfigurierbar oder erweiterbar").
            // Per-step charge/discharge coefficients are accumulated
            // from each active component first, then set on the solver
            // once — OR-Tools' SetCoefficient overwrites, so a single
            // pass keeps the contributions explicit.
            var (chargeCoef, dischargeCoef) = ComputeChargeDischargeCoefficients(
                request, n, dtHours);

            var objective = solver.Objective();
            for (var t = 0; t < n; t++)
            {
                objective.SetCoefficient(charge[t], chargeCoef[t]);
                objective.SetCoefficient(discharge[t], dischargeCoef[t]);
            }
            ApplySocTargetObjective(objective, slackBelow, slackAbove, capacityKwh);
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
                var components = ComputeObjectiveComponents(
                    request, charge, discharge, slackBelow, slackAbove, dtHours, capacityKwh);
                var rawTotal = components.Sum(c => c.Value);
                // Snap floating-point noise around a trivial optimum to
                // exact zero before persisting (review #18) — keeps a
                // flat-price idle run from surfacing a -1.4e-12 EUR
                // total that confuses dashboards. The snap applies only
                // to the run-level total; individual component values
                // carry their raw LP contributions so a breakdown sums
                // to the snapped total within epsilon.
                var snappedTotal = Math.Abs(rawTotal) < ObjectiveZeroEpsilon ? 0.0 : rawTotal;
                return BuildSolutionResult(
                    request, mappedStatus, terminationCode, terminationDetail,
                    charge, discharge, snappedTotal, components, elapsed);
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
        IReadOnlyList<OptimizationObjectiveComponent> components,
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

        var breakdown = new OptimizationObjectiveBreakdown(components);

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

    // Computes per-step objective coefficients on charge/discharge from
    // every active component that contributes to those variables. Each
    // component adds to the running totals, then SetCoefficient is called
    // exactly once per (variable, step) — OR-Tools' SetCoefficient
    // overwrites, so accumulating in arrays first keeps the contributions
    // RM-M4-02: per-step max-charge/max-discharge caps after deducting
    // held reserves (LH-MKT-004). A reserve covers step t if its
    // half-open window contains the step's start instant; multiple
    // reserves of the same Direction sum (e.g. FCR 5 kW + AFRR-Up
    // 3 kW at the same step). Symmetric reserves withhold capacity on
    // both sides; Up only on discharge; Down only on charge. The
    // returned arrays may carry negative values when reserves over-
    // commit a step — the caller surfaces that as
    // `reserve-exceeds-capacity` rather than letting it become an
    // LP-infeasible cap.
    private static (double[] EffChargeMax, double[] EffDischargeMax) ComputeReserveCaps(
        ScheduleOptimizationRequest request,
        int n)
    {
        var asset = request.Asset;
        var effChargeMax = new double[n];
        var effDischargeMax = new double[n];
        for (var t = 0; t < n; t++)
        {
            effChargeMax[t] = asset.MaxChargePowerKw;
            effDischargeMax[t] = asset.MaxDischargePowerKw;
        }

        if (request.Reserves.Count == 0)
        {
            return (effChargeMax, effDischargeMax);
        }

        for (var t = 0; t < n; t++)
        {
            var stepStart = request.HorizonStart + TimeSpan.FromTicks(t * request.TimeStep.Ticks);
            foreach (var band in request.Reserves)
            {
                if (!string.Equals(band.AssetId, asset.AssetId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!band.Covers(stepStart))
                {
                    continue;
                }
                switch (band.Direction)
                {
                    case ReserveDirection.Symmetric:
                        effChargeMax[t] -= band.PowerKw;
                        effDischargeMax[t] -= band.PowerKw;
                        break;
                    case ReserveDirection.Up:
                        effDischargeMax[t] -= band.PowerKw;
                        break;
                    case ReserveDirection.Down:
                        effChargeMax[t] -= band.PowerKw;
                        break;
                }
            }
        }
        return (effChargeMax, effDischargeMax);
    }

    // explicit and avoids read-modify-write against the solver state.
    private (double[] chargeCoef, double[] dischargeCoef) ComputeChargeDischargeCoefficients(
        ScheduleOptimizationRequest request,
        int n,
        double dtHours)
    {
        var chargeCoef = new double[n];
        var dischargeCoef = new double[n];

        // energy_cost: price[t] (EUR/MWh) * (charge − discharge) (kW) *
        // Δt (h) / 1000. Charging draws from the grid (cost), discharging
        // exports (revenue, negative cost).
        for (var t = 0; t < n; t++)
        {
            var energyCoef = request.PricesPerStep![t] * dtHours / 1000.0;
            chargeCoef[t] += energyCoef;
            dischargeCoef[t] -= energyCoef;
        }

        // degradation_cost (RM-M2-04): linear throughput proxy. Both
        // charge and discharge contribute their absolute kWh because both
        // stress the cells.
        if (_options.DegradationCost is { } degradation)
        {
            var degCoef = degradation.EurPerKwhThroughput * dtHours;
            for (var t = 0; t < n; t++)
            {
                chargeCoef[t] += degCoef;
                dischargeCoef[t] += degCoef;
            }
        }

        return (chargeCoef, dischargeCoef);
    }

    // Builds the optional slack variables for the SOC-target penalty.
    // Returns (null, null) when the option is not configured so callers
    // can skip the related objective and post-solve work cleanly.
    //
    // Slacks live for steps t in [0, n) and bind against soc[t+1] — the
    // SOC at the *end* of step t. soc[0] is fixed by the initial-SOC
    // constraint, so including it would add a fixed offset to the
    // objective that the optimiser cannot influence.
    private (Variable[]? below, Variable[]? above) BuildSocTargetSlacks(
        Solver solver,
        Variable[] soc,
        int n,
        double capacityKwh)
    {
        if (_options.SocTargetPenalty is not { } penalty)
        {
            return (null, null);
        }

        var targetKwh = penalty.TargetSocPercent / 100.0 * capacityKwh;
        var below = new Variable[n];
        var above = new Variable[n];
        for (var t = 0; t < n; t++)
        {
            below[t] = solver.MakeNumVar(0, double.PositiveInfinity, $"soc_below_{t}");
            above[t] = solver.MakeNumVar(0, double.PositiveInfinity, $"soc_above_{t}");

            // below[t] + soc[t+1] >= target  ⇔  below[t] >= target - soc[t+1]
            var belowCons = solver.MakeConstraint(
                targetKwh, double.PositiveInfinity, $"soc_below_c_{t}");
            belowCons.SetCoefficient(below[t], 1.0);
            belowCons.SetCoefficient(soc[t + 1], 1.0);

            // above[t] - soc[t+1] >= -target  ⇔  above[t] >= soc[t+1] - target
            var aboveCons = solver.MakeConstraint(
                -targetKwh, double.PositiveInfinity, $"soc_above_c_{t}");
            aboveCons.SetCoefficient(above[t], 1.0);
            aboveCons.SetCoefficient(soc[t + 1], -1.0);
        }
        return (below, above);
    }

    // Adds the SOC-target slack contribution to the LP objective. The
    // user-facing rate is EUR per percentage point of deviation; the
    // slack variables are kWh, so the coefficient converts: deviation
    // in percent = slack_kwh / capacity_kwh * 100, hence
    // EurPerKwhSlack = EurPerPercentDeviation * 100 / capacity_kwh.
    private void ApplySocTargetObjective(
        Objective objective,
        Variable[]? slackBelow,
        Variable[]? slackAbove,
        double capacityKwh)
    {
        if (_options.SocTargetPenalty is not { } penalty
            || slackBelow is null || slackAbove is null)
        {
            return;
        }

        var penaltyEurPerKwh = penalty.EurPerPercentDeviation * 100.0 / capacityKwh;
        for (var t = 0; t < slackBelow.Length; t++)
        {
            objective.SetCoefficient(slackBelow[t], penaltyEurPerKwh);
            objective.SetCoefficient(slackAbove[t], penaltyEurPerKwh);
        }
    }

    // Reconstructs each objective component from the LP solution. Per-
    // component values are computed from the SAME coefficients used in
    // ComputeChargeDischargeCoefficients / ApplySocTargetObjective, so
    // their sum equals the LP objective value within floating-point
    // epsilon. Component order is stable: energy_cost first, then
    // degradation_cost, then soc_target_penalty — matches the
    // configuration order callers will read in dashboards and the order
    // the persistence layer stores via the `position` column.
    private List<OptimizationObjectiveComponent> ComputeObjectiveComponents(
        ScheduleOptimizationRequest request,
        Variable[] charge,
        Variable[] discharge,
        Variable[]? slackBelow,
        Variable[]? slackAbove,
        double dtHours,
        double capacityKwh)
    {
        var n = request.StepCount;
        var components = new List<OptimizationObjectiveComponent>(capacity: 3);

        var energyCost = 0.0;
        for (var t = 0; t < n; t++)
        {
            var coef = request.PricesPerStep![t] * dtHours / 1000.0;
            energyCost += coef * (charge[t].SolutionValue() - discharge[t].SolutionValue());
        }
        components.Add(new OptimizationObjectiveComponent("energy_cost", energyCost, "EUR"));

        if (_options.DegradationCost is { } degradation)
        {
            var degCoef = degradation.EurPerKwhThroughput * dtHours;
            var degradationCost = 0.0;
            for (var t = 0; t < n; t++)
            {
                degradationCost += degCoef * (charge[t].SolutionValue() + discharge[t].SolutionValue());
            }
            components.Add(new OptimizationObjectiveComponent("degradation_cost", degradationCost, "EUR"));
        }

        if (_options.SocTargetPenalty is { } penalty
            && slackBelow is not null && slackAbove is not null)
        {
            var penaltyEurPerKwh = penalty.EurPerPercentDeviation * 100.0 / capacityKwh;
            var socPenalty = 0.0;
            for (var t = 0; t < n; t++)
            {
                socPenalty += penaltyEurPerKwh * (slackBelow[t].SolutionValue() + slackAbove[t].SolutionValue());
            }
            components.Add(new OptimizationObjectiveComponent("soc_target_penalty", socPenalty, "EUR"));
        }

        return components;
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
