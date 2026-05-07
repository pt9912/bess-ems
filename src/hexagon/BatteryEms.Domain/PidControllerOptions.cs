namespace BatteryEms.Domain;

public sealed record PidControllerOptions
{
    public double Kp { get; init; }
    public double Ki { get; init; }
    public double Kd { get; init; }

    // OutputMin/OutputMax are required so a caller cannot silently
    // disable LH-CTRL-004's output-clamping property by forgetting to
    // configure them. Both must be finite — pass tight engineering
    // bounds, or ±double.MaxValue if a particular consumer genuinely
    // wants effectively unbounded behaviour.
    public required double OutputMin { get; init; }
    public required double OutputMax { get; init; }

    // Symmetric absolute deadband on the error: when |error| <
    // DeadbandAbsolute the controller treats the error as 0 for P and
    // suppresses both the I update and the D term. The integrator's
    // value is held, and PreviousError is preserved so the derivative
    // across a deadband transition computes the actual error change on
    // exit. 0 disables the deadband.
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
        if (!double.IsFinite(OutputMin))
        {
            throw new ArgumentException(
                $"OutputMin must be finite (got {OutputMin}).",
                nameof(OutputMin));
        }
        if (!double.IsFinite(OutputMax))
        {
            throw new ArgumentException(
                $"OutputMax must be finite (got {OutputMax}).",
                nameof(OutputMax));
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
