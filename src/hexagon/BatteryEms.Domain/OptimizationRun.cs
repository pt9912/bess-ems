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

    // Termination is split into a low-cardinality Code (the dashboardable
    // grouping key, e.g. "or-tools-time-limit", "unsupported-price-unit")
    // and an optional Detail (e.g. "EUR/kWh", "5.000s > 2.000s") that
    // varies per run (review #16 / Option B). Persistence stores the
    // composed string in `optimization_runs.termination_reason`; the read
    // path reconstructs (Code, Detail) via ParseTerminationReason.
    public string TerminationCode { get; }
    public string? TerminationDetail { get; }

    // Combined wire form. Detail-bearing reasons render as "code:detail"
    // so the existing API + audit-log consumers see no breaking change.
    public string TerminationReason =>
        TerminationDetail is null ? TerminationCode : $"{TerminationCode}:{TerminationDetail}";

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
        string terminationCode,
        string? terminationDetail,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(terminationCode);
        if (terminationCode.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"TerminationCode must not contain ':'; the colon separates code from detail in the composed reason (got '{terminationCode}').",
                nameof(terminationCode));
        }
        // Codes are kebab-case identifiers used as dashboard grouping
        // keys (review #16). Reject control chars and cap the length so
        // a foreign writer (or a copy-paste bug) can't push a megabyte
        // of payload or an embedded newline through the column and
        // corrupt the read-side parser. 64 chars covers every code the
        // M2 producers emit by a comfortable margin.
        const int TerminationCodeMaxLength = 64;
        if (terminationCode.Length > TerminationCodeMaxLength)
        {
            throw new ArgumentException(
                $"TerminationCode length {terminationCode.Length} exceeds {TerminationCodeMaxLength}.",
                nameof(terminationCode));
        }
        foreach (var c in terminationCode)
        {
            if (char.IsControl(c))
            {
                throw new ArgumentException(
                    $"TerminationCode must not contain control characters (got '{terminationCode}').",
                    nameof(terminationCode));
            }
        }
        if (terminationDetail is not null && string.IsNullOrWhiteSpace(terminationDetail))
        {
            throw new ArgumentException(
                "TerminationDetail must be either null or a non-blank string.",
                nameof(terminationDetail));
        }
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
        TerminationCode = terminationCode;
        TerminationDetail = terminationDetail;
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

    // Inverse of TerminationReason: persistence reads back the composed
    // string from `optimization_runs.termination_reason` and reconstructs
    // (Code, Detail). Splits on the FIRST ':' so a Detail can itself
    // contain colons (e.g. an ISO timestamp later). Code may not contain
    // ':' (the constructor enforces it on the write side).
    public static (string Code, string? Detail) ParseTerminationReason(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        // Trim defensively: a foreign writer (M3 multi-writer scenario)
        // might have inserted leading/trailing whitespace that the
        // round-trip equality check would otherwise see as drift.
        var trimmed = raw.Trim();
        var idx = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (idx < 0)
        {
            return (trimmed, null);
        }
        var code = trimmed[..idx].TrimEnd();
        var detail = trimmed[(idx + 1)..].TrimStart();
        if (string.IsNullOrWhiteSpace(detail))
        {
            // "code:" with nothing after — treat as code-only so the
            // round-trip never produces a blank Detail that the
            // constructor would reject.
            return (code, null);
        }
        return (code, detail);
    }
}
