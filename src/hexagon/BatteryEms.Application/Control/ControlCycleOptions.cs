namespace BatteryEms.Application.Control;

public sealed record ControlCycleOptions(TimeSpan SafeFallbackValidity)
{
    public static ControlCycleOptions Default { get; } = new(TimeSpan.FromSeconds(5));
}
