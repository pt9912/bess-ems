namespace BatteryEms.Adapters.Optimization.Mpc.Local;

public sealed class LocalOsqpMpcSolverException : InvalidOperationException
{
    public string ReasonCode { get; }

    public LocalOsqpMpcSolverException()
        : this(LocalOsqpMpcReasonCodes.ModelInvalid, "Local OSQP MPC solver failed.")
    {
    }

    public LocalOsqpMpcSolverException(string message)
        : this(LocalOsqpMpcReasonCodes.ModelInvalid, message)
    {
    }

    public LocalOsqpMpcSolverException(string message, Exception innerException)
        : base(message, innerException)
    {
        ReasonCode = LocalOsqpMpcReasonCodes.ModelInvalid;
    }

    public LocalOsqpMpcSolverException(string reasonCode, string message)
        : base($"{reasonCode}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ReasonCode = reasonCode;
    }
}
