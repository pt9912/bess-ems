// Package scenario loads and validates simulator fixtures
// (plan-RM-M1-simulator.md §136). Validation is structural only — domain
// invariants live on the .NET-EMS side.
package scenario

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

var (
	// ErrMissingID is returned when a scenario fixture lacks an id.
	ErrMissingID = errors.New("scenario id missing")
	// ErrMissingName is returned when a scenario fixture lacks a name.
	ErrMissingName = errors.New("scenario name missing")
	// ErrMissingAssetID is returned when the asset has no asset_id.
	ErrMissingAssetID = errors.New("scenario asset.asset_id missing")
	// ErrEmptyTelemetry is returned when the telemetry sequence is empty.
	ErrEmptyTelemetry = errors.New("scenario telemetry empty")
	// ErrNonMonotonicOffsets is returned when telemetry offsets do not
	// strictly increase. Replay determinism depends on monotonic offsets.
	ErrNonMonotonicOffsets = errors.New("scenario telemetry offsets must strictly increase")
)

// LoadFromFile reads a scenario fixture from disk and validates it.
func LoadFromFile(path string) (model.Scenario, error) {
	data, err := os.ReadFile(path) //nolint:gosec // path is explicit operator input
	if err != nil {
		return model.Scenario{}, fmt.Errorf("read scenario %q: %w", path, err)
	}
	return Parse(data)
}

// Parse decodes a scenario fixture from raw JSON and validates it.
func Parse(data []byte) (model.Scenario, error) {
	var scn model.Scenario
	if err := json.Unmarshal(data, &scn); err != nil {
		return model.Scenario{}, fmt.Errorf("decode scenario: %w", err)
	}
	if err := Validate(scn); err != nil {
		return model.Scenario{}, err
	}
	return scn, nil
}

// Validate checks that a scenario is structurally complete enough for
// the simulator to drive a Modbus/MQTT replay. Domain invariants such
// as SOC ranges or efficiency bounds are NOT checked here; that lives
// on the .NET-EMS side via the JSON Schema.
func Validate(scn model.Scenario) error {
	if scn.ID == "" {
		return ErrMissingID
	}
	if scn.Name == "" {
		return ErrMissingName
	}
	if scn.Asset.AssetID == "" {
		return ErrMissingAssetID
	}
	if len(scn.Telemetry) == 0 {
		return ErrEmptyTelemetry
	}
	for i := 1; i < len(scn.Telemetry); i++ {
		if scn.Telemetry[i].OffsetMillis <= scn.Telemetry[i-1].OffsetMillis {
			return fmt.Errorf("%w: index %d", ErrNonMonotonicOffsets, i)
		}
	}
	return nil
}
