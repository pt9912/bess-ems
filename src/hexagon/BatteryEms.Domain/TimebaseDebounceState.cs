namespace BatteryEms.Domain;

public enum TimebaseHealth
{
    Healthy,
    Degraded,
}

// Debounce-State for the timebase-stability check that gates Regelleistung
// activations (plan-RM-M4-03 §144). 3-in-10 sliding-window violation
// trigger transitions Healthy -> Degraded; 5 consecutive stable cycles
// (or an explicit Recover()) transitions Degraded -> Healthy. Constants
// are domain-wired per D-04: the operator-tunable surface is the
// per-sample tolerances on RegelleistungOptions, not the debounce
// characteristic itself.
//
// Pure functional state: Observe()/Recover() return new instances,
// analog to PidController.Step.
public sealed record TimebaseDebounceState
{
    // Sliding window of the last up-to WindowLength cycles. Index 0 =
    // oldest. Each entry: true = violation cycle, false = stable cycle.
    // Used while Healthy to count violations within the window.
    // Cleared on the Healthy->Degraded transition; rebuilt as cycles
    // accrue after a Degraded->Healthy recovery.
    public required IReadOnlyList<bool> RecentCycles { get; init; }

    // Consecutive stable cycles since entering Degraded. Used to decide
    // recovery (>= StableRecoverThreshold). Reset to 0 on any violation
    // observed while Degraded; reset to 0 on Healthy->Degraded transition;
    // reset to 0 on recovery.
    public required int ConsecutiveStable { get; init; }

    public required TimebaseHealth Health { get; init; }

    public const int WindowLength = 10;
    public const int ViolationThreshold = 3;
    public const int StableRecoverThreshold = 5;

    public static TimebaseDebounceState Initial { get; } = new()
    {
        Health = TimebaseHealth.Healthy,
        RecentCycles = Array.Empty<bool>(),
        ConsecutiveStable = 0,
    };

    // Observe one cycle's outcome. violationThisCycle = true means the
    // cycle saw a timebase-stability violation (e.g. a sample with a
    // stale timestamp or out-of-tolerance future skew).
    public TimebaseDebounceState Observe(bool violationThisCycle)
    {
        if (Health == TimebaseHealth.Healthy)
        {
            var window = AppendBounded(RecentCycles, violationThisCycle, WindowLength);
            var violations = CountTrue(window);
            if (violations >= ViolationThreshold)
            {
                return new TimebaseDebounceState
                {
                    Health = TimebaseHealth.Degraded,
                    RecentCycles = Array.Empty<bool>(),
                    ConsecutiveStable = 0,
                };
            }
            return new TimebaseDebounceState
            {
                Health = TimebaseHealth.Healthy,
                RecentCycles = window,
                ConsecutiveStable = 0,
            };
        }

        // Degraded: track only the consecutive-stable run.
        if (violationThisCycle)
        {
            return ConsecutiveStable == 0
                ? this
                : this with { ConsecutiveStable = 0 };
        }

        var nextStable = ConsecutiveStable + 1;
        if (nextStable >= StableRecoverThreshold)
        {
            return Initial;
        }
        return this with { ConsecutiveStable = nextStable };
    }

    // Explicit recovery (operator/health-recover signal). Resets to a
    // clean Healthy state regardless of current condition.
    public TimebaseDebounceState Recover() => this with
    {
        Health = TimebaseHealth.Healthy,
        RecentCycles = Array.Empty<bool>(),
        ConsecutiveStable = 0,
    };

    private static bool[] AppendBounded(
        IReadOnlyList<bool> existing,
        bool next,
        int max)
    {
        var size = Math.Min(existing.Count + 1, max);
        var result = new bool[size];
        var skip = existing.Count + 1 - size;
        for (var i = 0; i < size - 1; i++)
        {
            result[i] = existing[i + skip];
        }
        result[size - 1] = next;
        return result;
    }

    private static int CountTrue(bool[] values)
    {
        var count = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i])
            {
                count++;
            }
        }
        return count;
    }
}
