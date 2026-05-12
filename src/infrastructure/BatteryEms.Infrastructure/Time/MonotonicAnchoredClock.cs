using System.Diagnostics;
using BatteryEms.Application.Time;

namespace BatteryEms.Infrastructure.Time;

public sealed class MonotonicAnchoredClock : IClock
{
    private readonly Func<long> _timestampProvider;
    private readonly double _ticksPerTimestamp;
    private readonly object _gate = new();
    private DateTimeOffset _anchorUtc;
    private long _anchorTimestamp;
    private DateTimeOffset _lastReturnedUtc;

    public MonotonicAnchoredClock()
        : this(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), Stopwatch.Frequency, Stopwatch.GetTimestamp)
    {
    }

    public MonotonicAnchoredClock(
        DateTimeOffset anchorUtc,
        long anchorTimestamp,
        long timestampFrequency,
        Func<long> timestampProvider)
    {
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency), "Timestamp frequency must be positive.");
        }
        ArgumentNullException.ThrowIfNull(timestampProvider);

        _anchorUtc = anchorUtc.ToUniversalTime();
        _anchorTimestamp = anchorTimestamp;
        _lastReturnedUtc = _anchorUtc;
        _timestampProvider = timestampProvider;
        _ticksPerTimestamp = (double)TimeSpan.TicksPerSecond / timestampFrequency;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                var projected = Project(_timestampProvider());
                if (projected < _lastReturnedUtc)
                {
                    return _lastReturnedUtc;
                }
                _lastReturnedUtc = projected;
                return projected;
            }
        }
    }

    public bool TryResync(DateTimeOffset observedUtc, TimeSpan maxAllowedDrift)
    {
        if (maxAllowedDrift < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAllowedDrift), "MaxAllowedDrift must be non-negative.");
        }

        lock (_gate)
        {
            var nowTimestamp = _timestampProvider();
            var projectedNow = Project(nowTimestamp);
            var drift = observedUtc.ToUniversalTime() - projectedNow;
            if (Abs(drift) > maxAllowedDrift)
            {
                return false;
            }

            var observed = observedUtc.ToUniversalTime();
            if (observed < _lastReturnedUtc)
            {
                observed = _lastReturnedUtc;
            }

            _anchorUtc = observed;
            _anchorTimestamp = nowTimestamp;
            return true;
        }
    }

    private DateTimeOffset Project(long timestamp)
    {
        var elapsedTimestamp = timestamp - _anchorTimestamp;
        var elapsedTicks = checked((long)Math.Round(elapsedTimestamp * _ticksPerTimestamp));
        return _anchorUtc.AddTicks(elapsedTicks);
    }

    private static TimeSpan Abs(TimeSpan value) =>
        value < TimeSpan.Zero ? value.Negate() : value;
}
