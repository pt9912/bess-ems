using BatteryEms.Application.Time;

namespace BatteryEms.Application.Api;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class DefaultHealthQuery : IHealthQuery
{
    private readonly IClock _clock;

    public DefaultHealthQuery(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public HealthStatus Probe() => new("ok", _clock.UtcNow);
}
