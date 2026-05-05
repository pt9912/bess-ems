// Package runtime is the simulator's composition seam: it walks the
// telemetry sequence in a scenario fixture and pushes each snapshot to
// the Modbus server and the MQTT publisher in lockstep with the
// fixture's offset_millis. plan-RM-M1-simulator.md §136 requires
// deterministic replay; sleeping is delegated to an injected Sleeper so
// unit tests do not block on wall-clock time.
package runtime

import (
	"context"
	"fmt"
	"time"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// ModbusApplier is implemented by the Modbus server. The interface
// avoids importing the modbus package here so depguard does not have to
// allow runtime → modbus + mqtt + model concurrently.
type ModbusApplier interface {
	Apply(snap model.TelemetrySnapshot)
}

// MqttPublisher is implemented by the MQTT publisher.
type MqttPublisher interface {
	PublishSnapshot(ctx context.Context, snap model.TelemetrySnapshot) error
}

// Sleeper waits the requested duration unless ctx is cancelled. Tests
// inject a NoSleep variant for determinism; production wires
// SleepWithContext.
type Sleeper func(ctx context.Context, d time.Duration) error

// SleepWithContext is the production Sleeper.
func SleepWithContext(ctx context.Context, d time.Duration) error {
	if d <= 0 {
		return nil
	}
	timer := time.NewTimer(d)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err() //nolint:wrapcheck // standard cancellation contract
	case <-timer.C:
		return nil
	}
}

// NoSleep returns immediately. Useful in tests that exercise iteration
// logic without wall-clock delay.
func NoSleep(_ context.Context, _ time.Duration) error {
	return nil
}

// Orchestrator wires modbus + mqtt + sleeper for one scenario run.
type Orchestrator struct {
	modbus  ModbusApplier
	mqtt    MqttPublisher
	sleeper Sleeper
}

// NewOrchestrator constructs an Orchestrator.
func NewOrchestrator(modbus ModbusApplier, mqtt MqttPublisher, sleeper Sleeper) *Orchestrator {
	if sleeper == nil {
		sleeper = SleepWithContext
	}
	return &Orchestrator{modbus: modbus, mqtt: mqtt, sleeper: sleeper}
}

// Run walks scn.Telemetry, sleeping between snapshots according to
// strictly-increasing OffsetMillis values. Modbus.Apply is always
// called first, then mqtt.PublishSnapshot. Any publisher error
// short-circuits the run; ctx cancellation interrupts the next sleep.
func (o *Orchestrator) Run(ctx context.Context, scn model.Scenario) error {
	var last int64
	for i, snap := range scn.Telemetry {
		delta := time.Duration(snap.OffsetMillis-last) * time.Millisecond
		if i > 0 && delta > 0 {
			if err := o.sleeper(ctx, delta); err != nil {
				return err //nolint:wrapcheck // preserve cancellation semantics
			}
		}

		if o.modbus != nil {
			o.modbus.Apply(snap)
		}
		if o.mqtt != nil {
			if err := o.mqtt.PublishSnapshot(ctx, snap); err != nil {
				return fmt.Errorf("publish snapshot %d: %w", i, err)
			}
		}

		last = snap.OffsetMillis
	}
	return nil
}
