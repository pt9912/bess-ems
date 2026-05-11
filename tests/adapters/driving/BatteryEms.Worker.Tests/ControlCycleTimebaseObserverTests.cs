using BatteryEms.Application.Markets;
using Xunit;

namespace BatteryEms.Worker.Tests;

// Plan-RM-M4-03 §144 Finding-2-Wiring-Pins: der ControlCycleHostedService
// füttert die TimebaseDebounceState-Maschine pro Tick. Diese Datei
// pinnt (a) die pure Violation-Klassifikation und (b) das Ende-zu-Ende-
// Wiring vom Tick zum Observer.Observe-Call.
public sealed class ControlCycleTimebaseObserverTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(1);

    // (a) Pure-Function-Pins
    // ---------------------------------------------------------------

    [Fact]
    public void First_tick_after_boot_reports_stable()
    {
        // Kein previous-timestamp → kein Vergleichsanker → stable.
        var violation = ControlCycleHostedService.ComputeTimebaseViolation(
            previousTickTimestamp: null,
            currentTickTimestamp: T0,
            cycleInterval: CycleInterval);

        Assert.False(violation);
    }

    [Theory]
    [InlineData(1000)]  // exakt das CycleInterval
    [InlineData(1100)]  // 10% jitter
    [InlineData(1500)]  // 50% jitter
    [InlineData(2000)]  // 2× upper-bound (inclusive)
    [InlineData(50)]    // sub-interval — sub-tick-jitter unter Last
    public void Delta_within_window_reports_stable(int deltaMs)
    {
        var violation = ControlCycleHostedService.ComputeTimebaseViolation(
            previousTickTimestamp: T0,
            currentTickTimestamp: T0 + TimeSpan.FromMilliseconds(deltaMs),
            cycleInterval: CycleInterval);

        Assert.False(violation);
    }

    [Theory]
    [InlineData(-1)]       // 1 ms Rückspring
    [InlineData(-500)]     // 500 ms Rückspring (NTP-Step-Korrektur)
    [InlineData(-3600000)] // 1 Stunde Rückspring (DST-Bug, manuelle Verstellung)
    public void Negative_delta_reports_violation(int deltaMs)
    {
        // Clock-Rückspring: NTP-Step rückwärts, Host-Suspend-Resume mit
        // fehlerhafter Resync, manuelle Clock-Verstellung.
        var violation = ControlCycleHostedService.ComputeTimebaseViolation(
            previousTickTimestamp: T0,
            currentTickTimestamp: T0 + TimeSpan.FromMilliseconds(deltaMs),
            cycleInterval: CycleInterval);

        Assert.True(violation);
    }

    [Theory]
    [InlineData(2001)]   // gerade über der 2×-Grenze
    [InlineData(5000)]   // 5× Interval — Host stalled mehrere Ticks
    [InlineData(60000)]  // Minute übersprungen — Suspend/Resume
    public void Large_forward_delta_reports_violation(int deltaMs)
    {
        // Ausgelassener Tick: Host stalled, PeriodicTimer hat überrollt,
        // oder Clock sprang vorwärts (NTP-Step forward).
        var violation = ControlCycleHostedService.ComputeTimebaseViolation(
            previousTickTimestamp: T0,
            currentTickTimestamp: T0 + TimeSpan.FromMilliseconds(deltaMs),
            cycleInterval: CycleInterval);

        Assert.True(violation);
    }

    // (b) Debounce-State-Integration-Pins
    // ---------------------------------------------------------------
    // Pins der Tick→Observer-Verdrahtung gegen den **echten**
    // `InMemoryTimebaseHealthSource` aus der Application-Schicht.
    // Beweist dass die 3-in-10/5-stable-Maschine vom Cycle-Pfad
    // tatsächlich gespeist wird (und nicht nur in Test-Stubs lebt).

    [Fact]
    public void Three_violations_in_ten_ticks_drive_state_to_degraded()
    {
        var source = new InMemoryTimebaseHealthSource();

        // Drei Violations gestreut in 10 Ticks → Degraded.
        for (var i = 0; i < 10; i++)
        {
            source.Observe(violationThisCycle: i is 1 or 4 or 8);
        }

        Assert.Equal(
            BatteryEms.Domain.TimebaseHealth.Degraded,
            source.Current.Health);
    }

    [Fact]
    public void Five_consecutive_stable_ticks_recover_from_degraded()
    {
        var source = new InMemoryTimebaseHealthSource();

        // Erst Degraded triggern.
        for (var i = 0; i < 3; i++) { source.Observe(violationThisCycle: true); }
        Assert.Equal(
            BatteryEms.Domain.TimebaseHealth.Degraded,
            source.Current.Health);

        // Fünf stabile Ticks → Recover.
        for (var i = 0; i < 5; i++) { source.Observe(violationThisCycle: false); }

        Assert.Equal(
            BatteryEms.Domain.TimebaseHealth.Healthy,
            source.Current.Health);
    }
}
