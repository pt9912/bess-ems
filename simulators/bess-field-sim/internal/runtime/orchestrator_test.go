package runtime_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/runtime"
)

type captureModbus struct {
	applied []model.TelemetrySnapshot
}

func (c *captureModbus) Apply(snap model.TelemetrySnapshot) {
	c.applied = append(c.applied, snap)
}

type capturePublisher struct {
	published []model.TelemetrySnapshot
	failOn    int
}

func (c *capturePublisher) PublishSnapshot(_ context.Context, snap model.TelemetrySnapshot) error {
	if c.failOn > 0 && len(c.published)+1 == c.failOn {
		return errors.New("fake publish failure")
	}
	c.published = append(c.published, snap)
	return nil
}

type captureSleeper struct {
	durations []time.Duration
	failAfter int
}

func (c *captureSleeper) sleep(_ context.Context, d time.Duration) error {
	c.durations = append(c.durations, d)
	if c.failAfter > 0 && len(c.durations) >= c.failAfter {
		return context.Canceled
	}
	return nil
}

func TestRun_AppliesAndPublishesEverySnapshot(t *testing.T) {
	t.Parallel()

	scn := model.Scenario{
		ID: "x", Name: "x",
		Asset:     model.BatteryAsset{AssetID: "a"},
		Telemetry: []model.TelemetrySnapshot{{OffsetMillis: 0}, {OffsetMillis: 1000}, {OffsetMillis: 2000}},
	}

	mb := &captureModbus{}
	pub := &capturePublisher{}
	o := runtime.NewOrchestrator(mb, pub, runtime.NoSleep)

	if err := o.Run(context.Background(), scn); err != nil {
		t.Fatalf("run: %v", err)
	}
	if len(mb.applied) != 3 {
		t.Errorf("modbus applied: want 3, got %d", len(mb.applied))
	}
	if len(pub.published) != 3 {
		t.Errorf("mqtt published: want 3, got %d", len(pub.published))
	}
}

func TestRun_SleepsByOffsetDelta(t *testing.T) {
	t.Parallel()

	scn := model.Scenario{
		ID: "x", Name: "x",
		Asset: model.BatteryAsset{AssetID: "a"},
		Telemetry: []model.TelemetrySnapshot{
			{OffsetMillis: 0},
			{OffsetMillis: 250},
			{OffsetMillis: 1500},
		},
	}

	sleeper := &captureSleeper{}
	o := runtime.NewOrchestrator(&captureModbus{}, &capturePublisher{}, sleeper.sleep)

	if err := o.Run(context.Background(), scn); err != nil {
		t.Fatalf("run: %v", err)
	}
	if len(sleeper.durations) != 2 {
		t.Fatalf("expected 2 sleeps (between 3 snapshots), got %d", len(sleeper.durations))
	}
	if sleeper.durations[0] != 250*time.Millisecond {
		t.Errorf("first sleep: want 250ms, got %v", sleeper.durations[0])
	}
	if sleeper.durations[1] != 1250*time.Millisecond {
		t.Errorf("second sleep: want 1250ms, got %v", sleeper.durations[1])
	}
}

func TestRun_PublisherErrorShortCircuits(t *testing.T) {
	t.Parallel()

	scn := model.Scenario{
		ID: "x", Name: "x",
		Asset: model.BatteryAsset{AssetID: "a"},
		Telemetry: []model.TelemetrySnapshot{
			{OffsetMillis: 0},
			{OffsetMillis: 100},
			{OffsetMillis: 200},
		},
	}

	mb := &captureModbus{}
	pub := &capturePublisher{failOn: 2}
	o := runtime.NewOrchestrator(mb, pub, runtime.NoSleep)

	err := o.Run(context.Background(), scn)
	if err == nil {
		t.Fatal("expected publisher error")
	}
	if len(mb.applied) != 2 {
		t.Errorf("modbus applied before failure: want 2, got %d", len(mb.applied))
	}
	if len(pub.published) != 1 {
		t.Errorf("mqtt published before failure: want 1, got %d", len(pub.published))
	}
}

func TestRun_CancelledContextFromSleeper(t *testing.T) {
	t.Parallel()

	scn := model.Scenario{
		ID: "x", Name: "x",
		Asset: model.BatteryAsset{AssetID: "a"},
		Telemetry: []model.TelemetrySnapshot{
			{OffsetMillis: 0},
			{OffsetMillis: 100},
			{OffsetMillis: 200},
		},
	}

	sleeper := &captureSleeper{failAfter: 1}
	o := runtime.NewOrchestrator(&captureModbus{}, &capturePublisher{}, sleeper.sleep)

	if err := o.Run(context.Background(), scn); !errors.Is(err, context.Canceled) {
		t.Fatalf("expected context.Canceled, got %v", err)
	}
}

func TestRun_HandlesEmptyScenario(t *testing.T) {
	t.Parallel()

	mb := &captureModbus{}
	pub := &capturePublisher{}
	o := runtime.NewOrchestrator(mb, pub, runtime.NoSleep)

	if err := o.Run(context.Background(), model.Scenario{}); err != nil {
		t.Fatalf("run: %v", err)
	}
	if len(mb.applied) != 0 || len(pub.published) != 0 {
		t.Error("empty scenario should produce no calls")
	}
}

func TestRun_NilDependenciesAreSkipped(t *testing.T) {
	t.Parallel()

	scn := model.Scenario{
		ID: "x", Name: "x",
		Asset:     model.BatteryAsset{AssetID: "a"},
		Telemetry: []model.TelemetrySnapshot{{OffsetMillis: 0}},
	}

	o := runtime.NewOrchestrator(nil, nil, runtime.NoSleep)
	if err := o.Run(context.Background(), scn); err != nil {
		t.Fatalf("run: %v", err)
	}
}

func TestNewOrchestrator_DefaultsSleeper(t *testing.T) {
	t.Parallel()

	o := runtime.NewOrchestrator(nil, nil, nil)
	if o == nil {
		t.Fatal("orchestrator nil")
	}
}

func TestSleepWithContext_NonPositiveReturnsImmediately(t *testing.T) {
	t.Parallel()

	if err := runtime.SleepWithContext(context.Background(), 0); err != nil {
		t.Errorf("expected nil for zero duration, got %v", err)
	}
	if err := runtime.SleepWithContext(context.Background(), -1*time.Second); err != nil {
		t.Errorf("expected nil for negative duration, got %v", err)
	}
}

func TestSleepWithContext_CancelledContext(t *testing.T) {
	t.Parallel()

	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if err := runtime.SleepWithContext(ctx, time.Hour); !errors.Is(err, context.Canceled) {
		t.Errorf("expected context.Canceled, got %v", err)
	}
}

func TestSleepWithContext_ShortDurationCompletes(t *testing.T) {
	t.Parallel()

	if err := runtime.SleepWithContext(context.Background(), time.Millisecond); err != nil {
		t.Errorf("expected nil, got %v", err)
	}
}
