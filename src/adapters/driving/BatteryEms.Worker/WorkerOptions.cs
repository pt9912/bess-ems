namespace BatteryEms.Worker;

// Bindable from IConfiguration section "Worker". CycleInterval defaults
// to 1 s — the regulation loop's nominal cadence per LH-CTRL-005. The
// host can shorten it for tests; production stays at 1 s.
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public TimeSpan CycleInterval { get; set; } = TimeSpan.FromSeconds(1);
}
