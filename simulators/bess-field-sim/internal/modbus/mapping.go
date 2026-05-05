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
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/safepath"
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
	// ErrMappingUnknownRegisterName is returned when a read-only register
	// cannot be sourced from TelemetrySnapshot.
	ErrMappingUnknownRegisterName = errors.New("modbus mapping unknown register name")
	// ErrMappingUnsupportedRegisterType is returned for register types the
	// simulator encoder cannot represent.
	ErrMappingUnsupportedRegisterType = errors.New("modbus mapping unsupported register type")
)

// LoadMapping reads a ModbusMapping fixture from disk and validates it.
func LoadMapping(path string) (model.ModbusMapping, error) {
	cleanPath, err := safepath.CleanRelative(path)
	if err != nil {
		return model.ModbusMapping{}, fmt.Errorf("validate mapping path: %w", err)
	}
	data, err := os.ReadFile(cleanPath)
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
	for i, reg := range m.Registers {
		if !reg.Writable && !isTelemetryRegister(reg.Name) {
			return fmt.Errorf("%w: index %d name=%q", ErrMappingUnknownRegisterName, i, reg.Name)
		}
		if !isSupportedRegisterType(reg.Type) {
			return fmt.Errorf("%w: index %d type=%q", ErrMappingUnsupportedRegisterType, i, reg.Type)
		}
	}
	return nil
}

func isTelemetryRegister(name string) bool {
	switch name {
	case "soc_percent",
		"soh_percent",
		"active_power_kw",
		"reactive_power_kvar",
		"dc_voltage",
		"dc_current",
		"temperature_celsius",
		"available",
		"fault_status":
		return true
	default:
		return false
	}
}

func isSupportedRegisterType(typ string) bool {
	switch typ {
	case "uint16", "int16", "uint32", "int32", "float32":
		return true
	default:
		return false
	}
}
