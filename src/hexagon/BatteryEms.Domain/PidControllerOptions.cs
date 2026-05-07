namespace BatteryEms.Domain;

public sealed record PidControllerOptions
{
    public double Kp { get; init; }
    public double Ki { get; init; }
    public double Kd { get; init; }
    public double OutputMin { get; init; } = double.NegativeInfinity;
    public double OutputMax { get; init; } = double.PositiveInfinity;

    // Symmetric absolute deadband on the error: when |error| <
    // DeadbandAbsolute the controller treats the error as 0 for P, I and
    // D. The integral state stays at its previous value, so the output
    // holds at the integrated position. 0 disables the deadband.
    public double DeadbandAbsolute { get; init; }

    public PidAntiWindupMode AntiWindupMode { get; init; } = PidAntiWindupMode.ConditionalIntegration;

    public PidControllerOptions EnsureValid()
    {
        if (!double.IsFinite(Kp))
        {
            throw new ArgumentException($"Kp must be finite (got {Kp}).", nameof(Kp));
        }
        if (!double.IsFinite(Ki))
        {
            throw new ArgumentException($"Ki must be finite (got {Ki}).", nameof(Ki));
        }
        if (!double.IsFinite(Kd))
        {
            throw new ArgumentException($"Kd must be finite (got {Kd}).", nameof(Kd));
        }
        if (double.IsNaN(OutputMin) || double.IsNaN(OutputMax))
        {
            throw new ArgumentException("OutputMin/OutputMax must not be NaN.", nameof(OutputMin));
        }
        if (OutputMin > OutputMax)
        {
            throw new ArgumentException(
                $"OutputMin ({OutputMin}) must not exceed OutputMax ({OutputMax}).",
                nameof(OutputMin));
        }
        if (!double.IsFinite(DeadbandAbsolute) || DeadbandAbsolute < 0)
        {
            throw new ArgumentException(
                $"DeadbandAbsolute must be finite and non-negative (got {DeadbandAbsolute}).",
                nameof(DeadbandAbsolute));
        }
        if (!Enum.IsDefined(AntiWindupMode))
        {
            throw new ArgumentException(
                $"Unknown AntiWindupMode '{AntiWindupMode}'.",
                nameof(AntiWindupMode));
        }
        return this;
    }
}
