using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class TimebaseDebounceStateTests
{
    [Fact]
    public void Initial_state_is_healthy_with_empty_window()
    {
        var state = TimebaseDebounceState.Initial;

        Assert.Equal(TimebaseHealth.Healthy, state.Health);
        Assert.Empty(state.RecentCycles);
        Assert.Equal(0, state.ConsecutiveStable);
    }

    [Fact]
    public void Constants_pin_master_dod_thresholds()
    {
        Assert.Equal(10, TimebaseDebounceState.WindowLength);
        Assert.Equal(3, TimebaseDebounceState.ViolationThreshold);
        Assert.Equal(5, TimebaseDebounceState.StableRecoverThreshold);
    }

    [Fact]
    public void Two_violations_in_ten_cycles_stay_healthy()
    {
        var state = TimebaseDebounceState.Initial
            .Observe(true)
            .Observe(false)
            .Observe(false)
            .Observe(true)
            .Observe(false);

        Assert.Equal(TimebaseHealth.Healthy, state.Health);
    }

    [Fact]
    public void Three_violations_within_window_transition_to_degraded()
    {
        var state = TimebaseDebounceState.Initial
            .Observe(true)
            .Observe(false)
            .Observe(true)
            .Observe(false)
            .Observe(true);

        Assert.Equal(TimebaseHealth.Degraded, state.Health);
        Assert.Empty(state.RecentCycles);
        Assert.Equal(0, state.ConsecutiveStable);
    }

    [Fact]
    public void Old_violations_age_out_of_window_and_keep_healthy()
    {
        // Two violations, then 9 stable cycles. The window length is 10,
        // so the last 10 cycles include the second violation but not the
        // first. Adding one more violation: last 10 = [violation, 8x
        // stable, violation] — only 2 in window, still healthy.
        var state = TimebaseDebounceState.Initial
            .Observe(true)
            .Observe(true);
        for (var i = 0; i < 9; i++)
        {
            state = state.Observe(false);
        }
        state = state.Observe(true);

        Assert.Equal(TimebaseHealth.Healthy, state.Health);
    }

    [Fact]
    public void Five_consecutive_stable_cycles_recover_from_degraded()
    {
        var state = ToDegraded();

        for (var i = 0; i < 5; i++)
        {
            state = state.Observe(false);
        }

        Assert.Equal(TimebaseHealth.Healthy, state.Health);
        Assert.Empty(state.RecentCycles);
        Assert.Equal(0, state.ConsecutiveStable);
    }

    [Fact]
    public void Violation_during_degraded_resets_stable_counter()
    {
        var state = ToDegraded()
            .Observe(false)
            .Observe(false)
            .Observe(false)
            .Observe(false);
        Assert.Equal(TimebaseHealth.Degraded, state.Health);
        Assert.Equal(4, state.ConsecutiveStable);

        state = state.Observe(true);

        Assert.Equal(TimebaseHealth.Degraded, state.Health);
        Assert.Equal(0, state.ConsecutiveStable);
    }

    [Fact]
    public void Explicit_recover_returns_to_initial_healthy()
    {
        var state = ToDegraded()
            .Observe(false)
            .Observe(false);

        var recovered = state.Recover();

        Assert.Equal(TimebaseHealth.Healthy, recovered.Health);
        Assert.Empty(recovered.RecentCycles);
        Assert.Equal(0, recovered.ConsecutiveStable);
    }

    [Fact]
    public void Recover_from_healthy_clears_window()
    {
        var state = TimebaseDebounceState.Initial
            .Observe(true)
            .Observe(true);

        var recovered = state.Recover();

        Assert.Equal(TimebaseHealth.Healthy, recovered.Health);
        Assert.Empty(recovered.RecentCycles);
    }

    // Mode pin: while Degraded the state must persist across mixed
    // observations until either StableRecoverThreshold consecutive
    // stable cycles or an explicit Recover(). This is the cross-slice
    // contract Sub-Slices C/D read to mark activations as not
    // dispatch-relevant — a single stable cycle (or a few interleaved
    // with violations) must not silently revert to Healthy.
    [Fact]
    public void Degraded_persists_under_interleaved_cycles_below_recover_threshold()
    {
        var state = ToDegraded()
            .Observe(false)
            .Observe(false)
            .Observe(true)
            .Observe(false)
            .Observe(true)
            .Observe(false);

        Assert.Equal(TimebaseHealth.Degraded, state.Health);
    }

    // Recovery semantics pin: the violation window must be cleared on
    // Healthy entry (whether via 5-stable or via explicit Recover), so
    // a single fresh violation post-recovery never combines with
    // pre-recovery violations to re-trigger Degraded.
    [Fact]
    public void Post_recover_window_is_clean_no_carryover_from_prior_violations()
    {
        var preRecover = TimebaseDebounceState.Initial
            .Observe(true)
            .Observe(true);
        var recovered = preRecover.Recover();

        var afterOneViolation = recovered.Observe(true);

        Assert.Equal(TimebaseHealth.Healthy, afterOneViolation.Health);
        Assert.Single(afterOneViolation.RecentCycles);
        Assert.True(afterOneViolation.RecentCycles[0]);
    }

    private static TimebaseDebounceState ToDegraded()
    {
        var state = TimebaseDebounceState.Initial
            .Observe(true)
            .Observe(true)
            .Observe(true);
        Assert.Equal(TimebaseHealth.Degraded, state.Health);
        return state;
    }
}
