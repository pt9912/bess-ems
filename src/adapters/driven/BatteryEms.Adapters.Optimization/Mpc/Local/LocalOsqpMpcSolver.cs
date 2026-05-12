using BatteryEms.Application.Mpc;
using OsqpNet;
using OsqpNet.Native;

namespace BatteryEms.Adapters.Optimization.Mpc.Local;

public sealed record LocalOsqpStrictSettings(
    bool WarmStart,
    int Scaling,
    bool Polish,
    int Threads);

public sealed class LocalOsqpMpcSolver : IMpcModelSolver
{
    public static readonly LocalOsqpStrictSettings StrictSettings =
        new(WarmStart: false, Scaling: 0, Polish: false, Threads: 1);

    private const double PowerRegularization = 1e-9;
    private const double ConstraintTolerance = 1e-9;
    private static readonly TimeSpan MinimumMeasurableSolverBudget = TimeSpan.FromMilliseconds(0.001);

    public Task<MpcTrajectory> SolveAsync(
        MpcState currentState,
        MpcModel model,
        MpcOptions options,
        DateTimeOffset trajectoryAnchor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateShape(currentState, model);
        ValidateSolverBudget(options);

        var problem = BuildProblem(currentState, model, options);
        using var p = ToCsc(problem.P);
        using var a = ToCsc(problem.A);
        using var solver = new OsqpSolver(
            p,
            problem.Q,
            a,
            problem.L,
            problem.U,
            BuildSettings(options));

        var status = solver.Solve();
        cancellationToken.ThrowIfCancellationRequested();

        if (status is not (OsqpStatus.Solved or OsqpStatus.SolvedInaccurate))
        {
            Throw(status, $"OSQP returned {status}.");
        }

        var solution = solver.GetPrimalSolution();
        return Task.FromResult(BuildTrajectory(solution, problem, options, trajectoryAnchor));
    }

    private static LocalOsqpProblem BuildProblem(
        MpcState currentState,
        MpcModel model,
        MpcOptions options)
    {
        var n = options.HorizonLength;
        var soc = BuildSocRows(currentState, model, n);
        PrecheckSocReachability(model, soc, n);
        var target = (model.Constraints.MinSocPercent + model.Constraints.MaxSocPercent) / 2.0;

        var p = NewMatrix(n, n);
        var q = new double[n];
        for (var k = 0; k < n; k++)
        {
            var offset = soc.Offsets[k] - target;
            for (var i = 0; i < n; i++)
            {
                q[i] += 2.0 * soc.Coefficients[k][i] * offset;
                for (var j = i; j < n; j++)
                {
                    p[i][j] += 2.0 * soc.Coefficients[k][i] * soc.Coefficients[k][j];
                }
            }
        }
        for (var i = 0; i < n; i++)
        {
            p[i][i] += 2.0 * PowerRegularization;
        }

        var rowCount = n + Math.Max(0, n - 1) + n;
        var a = NewMatrix(rowCount, n);
        var l = new double[rowCount];
        var u = new double[rowCount];
        var row = 0;

        for (var k = 0; k < n; k++, row++)
        {
            a[row][k] = 1.0;
            l[row] = model.Constraints.MinActivePowerKw;
            u[row] = model.Constraints.MaxActivePowerKw;
        }

        var rampDelta = model.Constraints.MaxRampKwPerSecond * options.SampleTime.TotalSeconds;
        for (var k = 1; k < n; k++, row++)
        {
            a[row][k] = 1.0;
            a[row][k - 1] = -1.0;
            l[row] = -rampDelta;
            u[row] = rampDelta;
        }

        for (var k = 0; k < n; k++, row++)
        {
            for (var j = 0; j < n; j++)
            {
                a[row][j] = soc.Coefficients[k][j];
            }
            l[row] = model.Constraints.MinSocPercent - soc.Offsets[k];
            u[row] = model.Constraints.MaxSocPercent - soc.Offsets[k];
        }

        return new LocalOsqpProblem(p, q, a, l, u, soc.Offsets, soc.Coefficients);
    }

    private static LocalOsqpSocRows BuildSocRows(MpcState currentState, MpcModel model, int horizonLength)
    {
        var offsets = new double[horizonLength];
        var coefficients = NewMatrix(horizonLength, horizonLength);

        var baseState = currentState.Mean.ToArray();
        for (var k = 0; k < horizonLength; k++)
        {
            baseState = Step(model, baseState, powerKw: 0.0);
            offsets[k] = OutputSoc(model, baseState, powerKw: 0.0);
        }

        for (var input = 0; input < horizonLength; input++)
        {
            var impulseState = currentState.Mean.ToArray();
            for (var k = 0; k < horizonLength; k++)
            {
                var power = k == input ? 1.0 : 0.0;
                impulseState = Step(model, impulseState, power);
                coefficients[k][input] = k < input
                    ? 0.0
                    : OutputSoc(model, impulseState, power) - offsets[k];
            }
        }

        return new LocalOsqpSocRows(offsets, coefficients);
    }

    private static void PrecheckSocReachability(MpcModel model, LocalOsqpSocRows soc, int horizonLength)
    {
        var constraints = model.Constraints;
        for (var k = 0; k < horizonLength; k++)
        {
            var minReachableSoc = soc.Offsets[k];
            var maxReachableSoc = soc.Offsets[k];
            for (var j = 0; j < horizonLength; j++)
            {
                var coefficient = soc.Coefficients[k][j];
                if (coefficient >= 0.0)
                {
                    minReachableSoc += coefficient * constraints.MinActivePowerKw;
                    maxReachableSoc += coefficient * constraints.MaxActivePowerKw;
                }
                else
                {
                    minReachableSoc += coefficient * constraints.MaxActivePowerKw;
                    maxReachableSoc += coefficient * constraints.MinActivePowerKw;
                }
            }

            if (maxReachableSoc < constraints.MinSocPercent - ConstraintTolerance ||
                minReachableSoc > constraints.MaxSocPercent + ConstraintTolerance)
            {
                Throw(
                    OsqpStatus.PrimalInfeasible,
                    $"SOC row {k} cannot reach the configured band before ramp constraints are applied.");
            }
        }
    }

    private static MpcTrajectory BuildTrajectory(
        double[] solution,
        LocalOsqpProblem problem,
        MpcOptions options,
        DateTimeOffset trajectoryAnchor)
    {
        var points = new MpcTrajectoryPoint[options.HorizonLength];
        for (var k = 0; k < options.HorizonLength; k++)
        {
            var soc = problem.SocOffsets[k];
            for (var j = 0; j < options.HorizonLength; j++)
            {
                soc += problem.SocCoefficients[k][j] * solution[j];
            }

            points[k] = new MpcTrajectoryPoint(
                trajectoryAnchor.AddTicks(options.SampleTime.Ticks * k),
                solution[k],
                soc);
        }

        return new MpcTrajectory(points, options.SampleTime);
    }

    private static double[] Step(MpcModel model, double[] state, double powerKw)
    {
        var next = new double[model.StateDimension];
        for (var row = 0; row < model.StateDimension; row++)
        {
            var value = 0.0;
            for (var col = 0; col < model.StateDimension; col++)
            {
                value += model.A[row, col] * state[col];
            }
            value += model.B[row, 0] * powerKw;
            next[row] = value;
        }
        return next;
    }

    private static double OutputSoc(MpcModel model, double[] state, double powerKw)
    {
        var value = 0.0;
        for (var col = 0; col < model.StateDimension; col++)
        {
            value += model.C[0, col] * state[col];
        }
        value += model.D[0, 0] * powerKw;
        return value;
    }

    private static OsqpSettings BuildSettings(MpcOptions options) =>
        new()
        {
            AllocateSolution = 1,
            LinsysSolver = OsqpLinsysSolver.Direct,
            Verbose = 0,
            WarmStarting = StrictSettings.WarmStart ? 1 : 0,
            Scaling = StrictSettings.Scaling,
            Polishing = StrictSettings.Polish ? 1 : 0,
            Rho = 0.1,
            RhoIsVec = 1,
            Sigma = 1e-6,
            Alpha = 1.6,
            CgMaxIter = 20,
            CgTolReduction = 10,
            CgTolFraction = 0.15,
            CgPrecond = OsqpPreconditioner.Diagonal,
            AdaptiveRho = 0,
            AdaptiveRhoInterval = 50,
            AdaptiveRhoFraction = 0.4,
            AdaptiveRhoTolerance = 5.0,
            MaxIter = options.Solver.MaxIterations,
            EpsAbs = Math.Max(options.Solver.OptimalityGap, 1e-9),
            EpsRel = Math.Max(options.Solver.OptimalityGap, 1e-9),
            EpsPrimInf = ConstraintTolerance,
            EpsDualInf = ConstraintTolerance,
            ScaledTermination = 0,
            CheckTermination = 25,
            CheckDualgap = 1,
            TimeLimit = options.Solver.TimeLimit.TotalSeconds,
            Delta = 1e-6,
            PolishRefineIter = 3,
        };

    private static CscMatrix ToCsc(double[][] dense)
    {
        var rows = dense.Length;
        var columns = dense[0].Length;
        var values = new List<double>();
        var rowIndices = new List<long>();
        var columnPointers = new long[columns + 1];

        for (var column = 0; column < columns; column++)
        {
            columnPointers[column] = values.Count;
            for (var row = 0; row < rows; row++)
            {
                var value = dense[row][column];
                if (Math.Abs(value) <= 0.0)
                {
                    continue;
                }
                values.Add(value);
                rowIndices.Add(row);
            }
        }
        columnPointers[columns] = values.Count;

        return new CscMatrix(rows, columns, values.ToArray(), rowIndices.ToArray(), columnPointers);
    }

    private static double[][] NewMatrix(int rows, int columns)
    {
        var matrix = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            matrix[row] = new double[columns];
        }
        return matrix;
    }

    private static void ValidateShape(MpcState currentState, MpcModel model)
    {
        if (currentState.Dimension != model.StateDimension)
        {
            ThrowModelInvalid(
                $"State dimension {currentState.Dimension} does not match model dimension {model.StateDimension}.");
        }
        if (model.InputDimension != 1)
        {
            ThrowModelInvalid(
                $"Local OSQP MPC adapter currently supports exactly one active-power input; got {model.InputDimension}.");
        }
        if (model.OutputDimension < 1)
        {
            ThrowModelInvalid("Model must expose SOC as output row 0.");
        }
    }

    private static void ValidateSolverBudget(MpcOptions options)
    {
        if (options.Solver.TimeLimit <= MinimumMeasurableSolverBudget)
        {
            Throw(
                OsqpStatus.TimeLimitReached,
                $"Configured solver time limit {options.Solver.TimeLimit} is below the local OSQP adapter budget floor.");
        }
    }

    private static void Throw(OsqpStatus status, string detail)
    {
        throw new LocalOsqpMpcSolverException(LocalOsqpMpcStatusMapper.Map(status), detail);
    }

    private static void ThrowModelInvalid(string detail) =>
        throw new LocalOsqpMpcSolverException(LocalOsqpMpcReasonCodes.ModelInvalid, detail);

    private sealed record LocalOsqpSocRows(double[] Offsets, double[][] Coefficients);

    private sealed record LocalOsqpProblem(
        double[][] P,
        double[] Q,
        double[][] A,
        double[] L,
        double[] U,
        double[] SocOffsets,
        double[][] SocCoefficients);
}
