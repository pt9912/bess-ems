namespace BatteryEms.Application.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
