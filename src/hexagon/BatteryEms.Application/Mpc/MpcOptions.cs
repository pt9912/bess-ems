using System;

namespace BatteryEms.Application.Mpc;

// D-04 reproducibility-mode slot. `Strict` is the default and is the
// only mode RM-M5-04 replay will accept; `BestEffort` documents the
// looser tolerance profile for hosts that explicitly opt out of single-
// thread solver runs; `None` is replay-untauglich and the Sub-Slice-D
// `MpcRun` write marks it so. Carry the enum on `MpcOptions` so the
// solver-config hash (D-09) consumes the deterministic-mode axis as a
// first-class input.
public enum DeterministicMode
{
    Strict,
    BestEffort,
    None,
}

// Solver knobs the Sub-Slice-B adapter consumes. Sub-Slice A only
// validates the slot shape; the values flow through to the solver via
// `IMpcModelSolver.SolveAsync` and feed the `solver_config_hash` axis
// of the D-09 identity tuple.
public sealed record MpcSolverOptions
{
    public TimeSpan TimeLimit { get; }
    public double OptimalityGap { get; }
    public int MaxIterations { get; }

    public MpcSolverOptions(TimeSpan timeLimit, double optimalityGap, int maxIterations)
    {
        if (timeLimit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeLimit), timeLimit, "TimeLimit must be positive.");
        if (!double.IsFinite(optimalityGap) || optimalityGap < 0)
            throw new ArgumentOutOfRangeException(nameof(optimalityGap), optimalityGap, "OptimalityGap must be a finite non-negative value.");
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations), maxIterations, "MaxIterations must be positive.");

        TimeLimit = timeLimit;
        OptimalityGap = optimalityGap;
        MaxIterations = maxIterations;
    }
}

// Kalman-filter shapes that Sub-Slice C's `DefaultLinearKalmanFilter`
// consumes (`P_0` is the initial covariance; `ProcessNoise` is Q;
// `MeasurementNoise` is R). Sub-Slice A only carries them through —
// the `IdentityStateEstimator` stub ignores Q and R — but the records
// already feed the D-09 `estimator_config_hash` so Sub-Slice C does
// not have to re-shape the options surface.
public sealed record MpcEstimatorOptions
{
    public MpcMatrix InitialCovariance { get; }
    public MpcMatrix ProcessNoise { get; }
    public MpcMatrix MeasurementNoise { get; }
    public int MaxConsecutiveMissingMeasurements { get; }

    public MpcEstimatorOptions(
        MpcMatrix initialCovariance,
        MpcMatrix processNoise,
        MpcMatrix measurementNoise,
        int maxConsecutiveMissingMeasurements)
    {
        ArgumentNullException.ThrowIfNull(initialCovariance);
        ArgumentNullException.ThrowIfNull(processNoise);
        ArgumentNullException.ThrowIfNull(measurementNoise);
        if (initialCovariance.Rows != initialCovariance.Columns)
            throw new ArgumentException("InitialCovariance must be square.", nameof(initialCovariance));
        if (processNoise.Rows != processNoise.Columns || processNoise.Rows != initialCovariance.Rows)
            throw new ArgumentException("ProcessNoise must be square and match InitialCovariance dimension.", nameof(processNoise));
        if (measurementNoise.Rows != measurementNoise.Columns)
            throw new ArgumentException("MeasurementNoise must be square.", nameof(measurementNoise));
        if (maxConsecutiveMissingMeasurements < 0)
            throw new ArgumentOutOfRangeException(nameof(maxConsecutiveMissingMeasurements), maxConsecutiveMissingMeasurements, "MaxConsecutiveMissingMeasurements must be non-negative.");

        InitialCovariance = initialCovariance;
        ProcessNoise = processNoise;
        MeasurementNoise = measurementNoise;
        MaxConsecutiveMissingMeasurements = maxConsecutiveMissingMeasurements;
    }
}

// Per-control-cycle configuration. `SampleTime` is the half-open
// segment width each trajectory point covers; `HorizonLength` is the
// number of segments the solver plans across. `RandomSeed` defaults to
// the D-09-derived seed (Sub-Slice D fills in the derivation; nullable
// here keeps the operator-override axis open without forcing Sub-Slice A
// to pin the derivation rule).
public sealed record MpcOptions
{
    public TimeSpan SampleTime { get; }
    public int HorizonLength { get; }
    public MpcSolverOptions Solver { get; }
    public MpcEstimatorOptions Estimator { get; }
    public DeterministicMode DeterministicMode { get; }
    public long? RandomSeedOverride { get; }

    public MpcOptions(
        TimeSpan sampleTime,
        int horizonLength,
        MpcSolverOptions solver,
        MpcEstimatorOptions estimator,
        DeterministicMode deterministicMode = DeterministicMode.Strict,
        long? randomSeedOverride = null)
    {
        if (sampleTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sampleTime), sampleTime, "SampleTime must be positive.");
        if (horizonLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(horizonLength), horizonLength, "HorizonLength must be positive.");
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(estimator);

        SampleTime = sampleTime;
        HorizonLength = horizonLength;
        Solver = solver;
        Estimator = estimator;
        DeterministicMode = deterministicMode;
        RandomSeedOverride = randomSeedOverride;
    }

    public TimeSpan Horizon => TimeSpan.FromTicks(SampleTime.Ticks * HorizonLength);
}
