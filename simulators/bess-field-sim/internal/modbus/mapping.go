// Package modbus exposes the simulator's Modbus TCP endpoint and the
// pure logic that translates a TelemetrySnapshot into Modbus register
// words via a ModbusMapping.
package modbus

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

var (
	// ErrMappingMissingProfile is returned when a mapping fixture lacks
	// profile_name.
	ErrMappingMissingProfile = errors.New("modbus mapping profile_name missing")
	// ErrMappingMissingDiscovery is returned when unit_id_discovery is missing.
	ErrMappingMissingDiscovery = errors.New("modbus mapping unit_id_discovery missing")
	// ErrMappingNoRegisters is returned when no registers are listed.
	ErrMappingNoRegisters = errors.New("modbus mapping has no registers")
	// ErrStaticDiscoveryWithoutUnitID is returned when unit_id_discovery=static
	// is set without static_unit_id.
	ErrStaticDiscoveryWithoutUnitID = errors.New("modbus mapping unit_id_discovery=static requires static_unit_id")
)

// LoadMapping reads a ModbusMapping fixture from disk and validates it.
func LoadMapping(path string) (model.ModbusMapping, error) {
	data, err := os.ReadFile(path) //nolint:gosec // path is explicit operator input
	if err != nil {
		return model.ModbusMapping{}, fmt.Errorf("read mapping %q: %w", path, err)
	}
	return ParseMapping(data)
}

// ParseMapping decodes and validates a ModbusMapping from raw JSON.
func ParseMapping(data []byte) (model.ModbusMapping, error) {
	var m model.ModbusMapping
	if err := json.Unmarshal(data, &m); err != nil {
		return model.ModbusMapping{}, fmt.Errorf("decode mapping: %w", err)
	}
	if err := ValidateMapping(m); err != nil {
		return model.ModbusMapping{}, err
	}
	return m, nil
}

// ValidateMapping checks structural invariants. Domain invariants stay
// on the .NET-EMS side (config/schema/modbus-mapping.schema.json).
func ValidateMapping(m model.ModbusMapping) error {
	if m.ProfileName == "" {
		return ErrMappingMissingProfile
	}
	if m.UnitIDDiscovery == "" {
		return ErrMappingMissingDiscovery
	}
	if len(m.Registers) == 0 {
		return ErrMappingNoRegisters
	}
	if m.UnitIDDiscovery == "static" && m.StaticUnitID == nil {
		return ErrStaticDiscoveryWithoutUnitID
	}
	return nil
}
