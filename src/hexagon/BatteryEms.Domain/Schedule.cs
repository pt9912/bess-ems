namespace BatteryEms.Domain;

public enum ScheduleType
{
    DayAhead,
    Intraday,
    RegelLeistungReserve,
}

// Half-open window [Start, End) per LH-MKT-007. Start and End are stored
// as DateTimeOffset; the schedule loader normalises everything to UTC so
// downstream code never has to reason about local time offsets.
public sealed record ScheduleWindow(
    DateTimeOffset Start,
    DateTimeOffset End,
    double TargetPowerKw)
{
    public bool Covers(DateTimeOffset moment) => moment >= Start && moment < End;
    public TimeSpan Duration => End - Start;
}

// Versioned, append-only schedule for one (asset, type) pair. Windows are
// half-open and must be chronologically ordered without gaps overlapping
// — these invariants are enforced at construction so the runtime can rely
// on them when resolving the active window for a moment.
public sealed class Schedule
{
    public string AssetId { get; }
    public ScheduleType Type { get; }
    public string MarketBidArea { get; }
    public int Version { get; }
    public IReadOnlyList<ScheduleWindow> Windows { get; }

    public Schedule(
        string assetId,
        ScheduleType type,
        string marketBidArea,
        int version,
        IReadOnlyList<ScheduleWindow> windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketBidArea);
        ArgumentNullException.ThrowIfNull(windows);
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be non-negative.");
        }
        if (windows.Count == 0)
        {
            throw new ArgumentException("Schedule must contain at least one window.", nameof(windows));
        }

        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (w.Start >= w.End)
            {
                throw new ArgumentException(
                    $"Window {i} has Start >= End ({w.Start:O} -> {w.End:O}).",
                    nameof(windows));
            }
            if (i > 0 && windows[i - 1].End > w.Start)
            {
                throw new ArgumentException(
                    $"Windows are not chronologically ordered without overlap at index {i}.",
                    nameof(windows));
            }
        }

        AssetId = assetId;
        Type = type;
        MarketBidArea = marketBidArea;
        Version = version;
        Windows = windows;
    }

    // Returns the window covering the moment, or null when the moment lies
    // before the first window or after the last. Linear scan is fine for
    // M1 (24-96 windows per schedule); persistence-backed queries can
    // index later if a horizon ever grows enough to matter.
    public ScheduleWindow? WindowCovering(DateTimeOffset moment)
    {
        foreach (var window in Windows)
        {
            if (window.Covers(moment))
            {
                return window;
            }
        }
        return null;
    }

    public DateTimeOffset HorizonStart => Windows[0].Start;
    public DateTimeOffset HorizonEnd => Windows[^1].End;
}
