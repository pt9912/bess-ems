namespace BatteryEms.Domain;

// Identifies the schedule a run consumed as input. Asset + version make
// the input unambiguous — the run's reproducibility test (LH-OPT-009)
// hinges on being able to point back at the exact predecessor schedule
// when the optimiser re-runs.
public sealed record ScheduleReference(string AssetId, ScheduleType Type, int Version)
{
    public ScheduleReference EnsureValid()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AssetId);
        if (Version < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Version), "Schedule version must be non-negative.");
        }
        return this;
    }
}

// Outputs of a schedule-optimisation run, with everything LH-OPT-009
// requires: RunId, input versions, solver name + status, horizon and
// time step, objective value + breakdown, constraint violations,
// runtime + termination reason, and the schedule the run produced.
//
// The record is immutable: an OptimizationRun is finalised once the
// solver returned (or crashed). Run-level mutation goes via "append a
// new run" rather than editing an existing one — the same append-only
// stance LH-PERSIST-007 takes.
public sealed class OptimizationRun
{
    public Guid RunId { get; }
    public string AssetId { get; }
    public string SolverName { get; }
    public OptimizationSolverStatus Status { get; }
    public DateTimeOffset HorizonStart { get; }
    public DateTimeOffset HorizonEnd { get; }
    public TimeSpan TimeStep { get; }
    public double ObjectiveValue { get; }
    public OptimizationObjectiveBreakdown ObjectiveBreakdown { get; }
    public IReadOnlyList<string> ConstraintViolations { get; }
    public IReadOnlyList<string> Warnings { get; }
    public TimeSpan SolverRuntime { get; }
    public string TerminationReason { get; }
    public DateTimeOffset CreatedAt { get; }

    // Schedules consumed as input to the run. Empty when the run started
    // from scratch (cold start). When non-empty, every entry is the
    // (asset, type, version) tuple of a Schedule that influenced the
    // result, e.g. the prior day-ahead schedule the optimiser carried
    // forward.
    public IReadOnlyList<ScheduleReference> Inputs { get; }

    // Schedule the run produced. Null when the solver did not emit a
    // schedule (e.g. Status=Infeasible / Failed); callers must inspect
    // Status before consuming the result.
    public ScheduleReference? ProducedSchedule { get; }

    public OptimizationRun(
        Guid runId,
        string assetId,
        string solverName,
        OptimizationSolverStatus status,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd,
        TimeSpan timeStep,
        double objectiveValue,
        OptimizationObjectiveBreakdown objectiveBreakdown,
        IReadOnlyList<string> constraintViolations,
        IReadOnlyList<string> warnings,
        TimeSpan solverRuntime,
        string terminationReason,
        DateTimeOffset createdAt,
        IReadOnlyList<ScheduleReference> inputs,
        ScheduleReference? producedSchedule)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId must not be empty.", nameof(runId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(solverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminationReason);
        ArgumentNullException.ThrowIfNull(objectiveBreakdown);
        ArgumentNullException.ThrowIfNull(constraintViolations);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(inputs);

        if (horizonStart >= horizonEnd)
        {
            throw new ArgumentException(
                $"HorizonStart must be before HorizonEnd ({horizonStart:O} -> {horizonEnd:O}).",
                nameof(horizonStart));
        }
        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeStep), "TimeStep must be positive (LH-OPT-008).");
        }
        if (solverRuntime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(solverRuntime), "SolverRuntime must be non-negative.");
        }
        if (!double.IsFinite(objectiveValue))
        {
            throw new ArgumentException(
                $"ObjectiveValue must be finite (got '{objectiveValue}').",
                nameof(objectiveValue));
        }

        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            input.EnsureValid();
        }
        producedSchedule?.EnsureValid();

        // A solver that reports Optimal/Feasible must hand back a
        // schedule; statuses without a usable solution must explicitly
        // report null. This catches the "solver said Optimal but emitted
        // nothing" wiring mistake at construction time.
        var hasSolution = status is OptimizationSolverStatus.Optimal
                          or OptimizationSolverStatus.Feasible;
        if (hasSolution && producedSchedule is null)
        {
            throw new ArgumentException(
                $"Status '{status}' requires a ProducedSchedule.",
                nameof(producedSchedule));
        }

        RunId = runId;
        AssetId = assetId;
        SolverName = solverName;
        Status = status;
        HorizonStart = horizonStart;
        HorizonEnd = horizonEnd;
        TimeStep = timeStep;
        ObjectiveValue = objectiveValue;
        ObjectiveBreakdown = objectiveBreakdown;
        ConstraintViolations = constraintViolations;
        Warnings = warnings;
        SolverRuntime = solverRuntime;
        TerminationReason = terminationReason;
        CreatedAt = createdAt;
        Inputs = inputs;
        ProducedSchedule = producedSchedule;
    }

    public TimeSpan Horizon => HorizonEnd - HorizonStart;

    // True when the solver returned a usable schedule. Lets callers do
    // `if (run.HasUsableSolution) { … }` without re-reading the status
    // taxonomy.
    public bool HasUsableSolution =>
        Status is OptimizationSolverStatus.Optimal or OptimizationSolverStatus.Feasible;
}
