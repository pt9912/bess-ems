namespace BatteryEms.Adapters.Optimization.Mpc.Local;

using OsqpNet.Native;

public static class LocalOsqpMpcReasonCodes
{
    public const string Optimal = "mpc-osqp-optimal";
    public const string TimeLimit = "mpc-osqp-time-limit";
    public const string Infeasible = "mpc-osqp-infeasible";
    public const string Unbounded = "mpc-osqp-unbounded";
    public const string NonConvex = "mpc-osqp-non-convex";
    public const string ModelInvalid = "mpc-osqp-model-invalid";
    public const string Interrupted = "mpc-osqp-interrupted";
    public const string Unsolved = "mpc-osqp-unsolved";
}

public static class LocalOsqpMpcStatusMapper
{
    public static string Map(OsqpStatus status) =>
        status switch
        {
            OsqpStatus.Solved or OsqpStatus.SolvedInaccurate => LocalOsqpMpcReasonCodes.Optimal,
            OsqpStatus.MaxIterReached or OsqpStatus.TimeLimitReached => LocalOsqpMpcReasonCodes.TimeLimit,
            OsqpStatus.PrimalInfeasible or OsqpStatus.PrimalInfeasibleInaccurate => LocalOsqpMpcReasonCodes.Infeasible,
            OsqpStatus.DualInfeasible or OsqpStatus.DualInfeasibleInaccurate => LocalOsqpMpcReasonCodes.Unbounded,
            OsqpStatus.NonCvx => LocalOsqpMpcReasonCodes.NonConvex,
            OsqpStatus.SigInt => LocalOsqpMpcReasonCodes.Interrupted,
            OsqpStatus.Unsolved => LocalOsqpMpcReasonCodes.Unsolved,
            _ => LocalOsqpMpcReasonCodes.ModelInvalid,
        };
}
