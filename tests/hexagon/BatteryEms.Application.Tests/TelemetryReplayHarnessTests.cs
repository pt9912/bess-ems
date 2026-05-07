using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

// RM-M2-10 / LH-TEST-004: acceptance "Messdaten-Replay erzeugt
// reproduzierbare Commands." Three tests against the regelzyklus path:
//
//   1. Reproducibility — two fresh harnesses on the same fixture
//      produce bit-exact identical command sequences (analogous to
//      OP-09 for the schedule solver).
//   2. Golden trace — the schedule-following dispatch + limiter chain
//      produces a hardcoded expected command sequence, so future
//      refactors to ConstraintLimiter / RampLimiter / mode logic
//      surface as a test failure.
//   3. Recovery — a sequence that starts with a missing snapshot then
//      pumps valid telemetry produces safe-stop first, normal dispatch
//      second; the sequence is reproducible.
public sealed class TelemetryReplayHarnessTests
{
    private const string AssetId = "asset-1";

    private static readonly DateTimeOffset Start =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Replay_of_same_fixture_yields_bit_exact_command_sequence()
    {
        // Two fresh harnesses, identical fixture, identical commands —
        // this is the acceptance for "reproducible commands". Catches
        // any hidden non-determinism (clock pull-through, GUID, hash-
        // randomized ordering) that would otherwise be invisible.
        var fixture = BuildLinearFixture();
        var asset = TestFixtures.CreateAsset(AssetId);

        var first = await new TelemetryReplayHarness(asset, new NoOpDispatchOptimizer())
            .RunAsync(AssetId, fixture, CancellationToken.None);
        var second = await new TelemetryReplayHarness(asset, new NoOpDispatchOptimizer())
            .RunAsync(AssetId, fixture, CancellationToken.None);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            // Records compare structurally by all properties — covers
            // CommandId, Timestamp, Mode, ActivePowerKw, ValidUntil,
            // Reason and Source in one assertion.
            Assert.Equal(first[i], second[i]);
        }
    }

    [Fact]
    public async Task Schedule_following_replay_matches_golden_trace()
    {
        // Pin the dispatch + limiter behaviour with a 4-step DayAhead
        // schedule. ConstraintLimiter sees telemetry within the SOC
        // band, so no clamps fire; RampLimiter has 100 kW/sec budget
        // and the schedule's largest step (50 kW between ticks) fits.
        // The expected commands are hand-computed from these inputs;
        // a future refactor that altered the dispatch reason format,
        // mode mapping, or any limiter clamp would break this test.
        var asset = TestFixtures.CreateAsset(AssetId);
        var schedule = new Schedule(
            AssetId,
            ScheduleType.DayAhead,
            "DE-LU",
            version: 1,
            new List<ScheduleWindow>
            {
                new(Start, Start + TimeSpan.FromSeconds(1), 10),
                new(Start + TimeSpan.FromSeconds(1), Start + TimeSpan.FromSeconds(2), 30),
                new(Start + TimeSpan.FromSeconds(2), Start + TimeSpan.FromSeconds(3), -20),
                new(Start + TimeSpan.FromSeconds(3), Start + TimeSpan.FromSeconds(4), 0),
            });

        var fixture = new[]
        {
            Record(Start, Telemetry()),
            Record(Start + TimeSpan.FromSeconds(1), Telemetry()),
            Record(Start + TimeSpan.FromSeconds(2), Telemetry()),
            Record(Start + TimeSpan.FromSeconds(3), Telemetry()),
        };

        var harness = new TelemetryReplayHarness(
            asset,
            new ScheduleFollowingDispatchOptimizer(),
            new[] { schedule });

        var commands = await harness.RunAsync(AssetId, fixture, CancellationToken.None);

        Assert.Equal(4, commands.Count);

        // Tick 0 — schedule says 10 kW discharge; first tick has no
        // previous power so RampLimiter is a no-op.
        Assert.Equal(10.0, commands[0].ActivePowerKw);
        Assert.Equal(CommandMode.Discharge, commands[0].Mode);
        Assert.Equal("follows-day-ahead-binding-rank-4", commands[0].Reason);
        Assert.Equal(CommandSource.Optimization, commands[0].Source);

        // Tick 1 — schedule says 30 kW; delta 20 kW within 100 kW/sec
        // budget over 1 s.
        Assert.Equal(30.0, commands[1].ActivePowerKw);
        Assert.Equal(CommandMode.Discharge, commands[1].Mode);

        // Tick 2 — schedule says -20 kW (charge); delta 50 kW within
        // 100 kW/sec budget.
        Assert.Equal(-20.0, commands[2].ActivePowerKw);
        Assert.Equal(CommandMode.Charge, commands[2].Mode);

        // Tick 3 — schedule says 0 kW; delta 20 kW within budget.
        Assert.Equal(0.0, commands[3].ActivePowerKw);
        Assert.Equal(CommandMode.Idle, commands[3].Mode);
    }

    [Fact]
    public async Task Missing_then_valid_recovery_produces_safe_stop_then_normal_dispatch_reproducibly()
    {
        // Sequence: tick 0 has no telemetry pumped → snapshot store
        // empty → ControlCycleUseCase emits safe-stop "no-snapshot".
        // Tick 1 pumps fresh telemetry → cycle dispatches normally
        // (Idle through NoOp). The recovery is reproducible across
        // replays — important because the production failure mode
        // (transient telemetry gap) must yield the same audit trail
        // every time.
        var asset = TestFixtures.CreateAsset(AssetId);
        var fixture = new[]
        {
            Record(Start, telemetry: null),                                          // missing snapshot
            Record(Start + TimeSpan.FromSeconds(1), Telemetry(timestamp: Start + TimeSpan.FromSeconds(1))),
        };

        async Task<IReadOnlyList<BatteryCommand>> RunOnce()
        {
            var harness = new TelemetryReplayHarness(asset, new NoOpDispatchOptimizer());
            return await harness.RunAsync(AssetId, fixture, CancellationToken.None);
        }

        var first = await RunOnce();
        var second = await RunOnce();

        // Tick 0 — safe-stop with reason "no-snapshot". Mode is the
        // load-bearing signal that distinguishes safe-stop from a
        // dispatched command (ActivePowerKw is 0 on either by
        // construction of NoOpDispatchOptimizer, so an
        // ActivePowerKw==0 assertion would be tautological).
        Assert.Equal(CommandMode.Stop, first[0].Mode);
        Assert.Equal("no-snapshot", first[0].Reason);
        Assert.Equal(CommandSource.Fallback, first[0].Source);

        // Tick 1 — normal dispatch (NoOp returns Idle).
        Assert.Equal(CommandMode.Idle, first[1].Mode);
        Assert.Equal(CommandSource.Optimization, first[1].Source);

        // Reproducibility across replays.
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Stale_then_valid_recovery_produces_safe_stop_then_normal_dispatch_reproducibly()
    {
        // Distinct production failure mode from the missing-snapshot
        // case above: telemetry IS pumped, but its receivedAt is older
        // than the staleness window the harness configures (10 s),
        // so the store flips Quality to Stale and the cycle short-
        // circuits with reason "snapshot-aged-...". Tick 1 pumps fresh
        // telemetry inside the freshness window → normal dispatch.
        // The audit reason must be reproducible across replays so
        // dashboards can correlate the same operational event.
        var asset = TestFixtures.CreateAsset(AssetId);
        var staleAt = Start - TimeSpan.FromSeconds(15); // > 10 s MaxAge configured by the harness
        var fixture = new[]
        {
            new TelemetryReplayRecord(
                Timestamp: Start,
                Telemetry: Telemetry(timestamp: staleAt),
                ReceivedAt: staleAt),
            Record(
                Start + TimeSpan.FromSeconds(1),
                Telemetry(timestamp: Start + TimeSpan.FromSeconds(1))),
        };

        async Task<IReadOnlyList<BatteryCommand>> RunOnce()
        {
            var harness = new TelemetryReplayHarness(asset, new NoOpDispatchOptimizer());
            return await harness.RunAsync(AssetId, fixture, CancellationToken.None);
        }

        var first = await RunOnce();
        var second = await RunOnce();

        // Tick 0 — safe-stop with stale-aged reason. Pin the format
        // shape (decimal seconds with one fractional digit followed
        // by 's'); a future refactor that changes the precision —
        // e.g. F1 → F2 or integer seconds — must update this regex
        // alongside the producer in InMemorySnapshotStore so the
        // dashboard contract stays in sync.
        Assert.Equal(CommandMode.Stop, first[0].Mode);
        Assert.Matches(@"^snapshot-aged-\d+\.\d+s$", first[0].Reason);
        Assert.Equal(CommandSource.Fallback, first[0].Source);

        // Tick 1 — fresh telemetry → normal dispatch.
        Assert.Equal(CommandMode.Idle, first[1].Mode);
        Assert.Equal(CommandSource.Optimization, first[1].Source);

        // Reproducibility across replays — including the formatted
        // staleness-seconds suffix in the reason string, which is
        // derived from clock arithmetic and must match bit-exactly.
        Assert.Equal(first, second);
    }

    private static IReadOnlyList<TelemetryReplayRecord> BuildLinearFixture() =>
        new[]
        {
            Record(Start, Telemetry()),
            Record(Start + TimeSpan.FromSeconds(1), Telemetry()),
            Record(Start + TimeSpan.FromSeconds(2), Telemetry()),
        };

    private static TelemetryReplayRecord Record(DateTimeOffset timestamp, BatteryTelemetry? telemetry) =>
        new(timestamp, telemetry);

    private static BatteryTelemetry Telemetry(DateTimeOffset? timestamp = null) =>
        TestFixtures.CreateTelemetry(AssetId) with { Timestamp = timestamp ?? Start };
}
