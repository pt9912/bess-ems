package modbus

import (
	"math"
	"sort"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// Wire vocabulary for register_table / word_order — mirrors the mapping
// schema enums (config/schema/modbus-mapping.schema.json). Empty values in a
// mapping resolve to the same defaults the .NET loader applies
// (ModbusRegisterMapping: holding / high_low), so existing profiles behave
// byte-identically.
const (
	TableHolding = "holding"
	TableInput   = "input"
	OrderHighLow = "high_low"
	OrderLowHigh = "low_high"
)

// RegisterTableOf resolves a register's table with the loader default.
func RegisterTableOf(reg model.ModbusRegister) string {
	if reg.RegisterTable == "" {
		return TableHolding
	}
	return reg.RegisterTable
}

// WordOrderOf resolves a register's 32-bit word order with the loader default.
func WordOrderOf(reg model.ModbusRegister) string {
	if reg.WordOrder == "" {
		return OrderHighLow
	}
	return reg.WordOrder
}

// Image is the encoded register wire image, split by register table: the
// two tables are separate Modbus address spaces (the HIL profile maps input
// address 1 AND holding address 1), so a merged map would corrupt them.
type Image struct {
	Holding map[int]uint16
	Input   map[int]uint16
}

// EncodeSnapshot returns the per-table address→word image for the read-only
// registers listed in the mapping that have a corresponding
// TelemetrySnapshot field. Multi-word types occupy consecutive addresses in
// the register's word_order (ADR 0013 §5.4: the encoder honors the mapping's
// word_order/register_table — the hand-mirror that dropped both fields was
// the ADR's motivating drift).
func EncodeSnapshot(snap model.TelemetrySnapshot, mapping model.ModbusMapping) Image {
	image := Image{
		Holding: make(map[int]uint16),
		Input:   make(map[int]uint16),
	}
	for _, reg := range mapping.Registers {
		if reg.Writable {
			continue
		}
		value, ok := valueFor(snap, reg.Name)
		if !ok {
			continue
		}
		raw := value
		if reg.ScaleFactor != 0 {
			raw = value / reg.ScaleFactor
		}
		var out map[int]uint16
		switch RegisterTableOf(reg) {
		case TableInput:
			out = image.Input
		default:
			out = image.Holding
		}
		writeRegister(out, reg.Address, reg.Type, WordOrderOf(reg), raw)
	}
	return image
}

// telemetryAccessors is the SINGLE canonical name→snapshot-field mapping.
// valueFor, isTelemetryRegister (mapping.go) and TelemetryRegisterNames all
// derive from it — a second hand-maintained name list was exactly the
// mirror-drift class this slice exists to end (second-review finding 1).
func telemetryAccessors() map[string]func(model.TelemetrySnapshot) float64 {
	return map[string]func(model.TelemetrySnapshot) float64{
		"soc_percent":         func(s model.TelemetrySnapshot) float64 { return s.SocPercent },
		"soh_percent":         func(s model.TelemetrySnapshot) float64 { return s.SohPercent },
		"active_power_kw":     func(s model.TelemetrySnapshot) float64 { return s.ActivePowerKw },
		"reactive_power_kvar": func(s model.TelemetrySnapshot) float64 { return s.ReactivePowerKvar },
		"dc_voltage":          func(s model.TelemetrySnapshot) float64 { return s.DcVoltage },
		"dc_current":          func(s model.TelemetrySnapshot) float64 { return s.DcCurrent },
		"temperature_celsius": func(s model.TelemetrySnapshot) float64 { return s.TemperatureCelsius },
		"available": func(s model.TelemetrySnapshot) float64 {
			if s.Available {
				return 1
			}
			return 0
		},
		"fault_status": func(s model.TelemetrySnapshot) float64 {
			if s.FaultStatus == "" || s.FaultStatus == "ok" {
				return 0
			}
			return 1
		},
	}
}

func valueFor(snap model.TelemetrySnapshot, name string) (float64, bool) {
	accessor, ok := telemetryAccessors()[name]
	if !ok {
		return 0, false
	}
	return accessor(snap), true
}

// TelemetryRegisterNames returns the snapshot-backed register names the
// simulator can serve, derived from the canonical accessor map (sorted for
// determinism). Exported so the golden-vector conformance check scopes
// itself to sim-served registers without a second list.
func TelemetryRegisterNames() []string {
	accessors := telemetryAccessors()
	names := make([]string, 0, len(accessors))
	for name := range accessors {
		names = append(names, name)
	}
	sort.Strings(names)
	return names
}

func writeRegister(out map[int]uint16, addr int, typ, order string, raw float64) {
	switch typ {
	case "uint16":
		out[addr] = uint16(raw)
	case "int16":
		out[addr] = uint16(int16(raw))
	case "uint32":
		write32(out, addr, order, uint32(raw))
	case "int32":
		write32(out, addr, order, uint32(int32(raw)))
	case "float32":
		write32(out, addr, order, math.Float32bits(float32(raw)))
	}
}

// write32 places the two 16-bit halves in transmission order: high_low puts
// the most-significant word at the start address (Big-Endian Modbus
// convention), low_high swaps the halves (HIL-class devices).
func write32(out map[int]uint16, addr int, order string, v uint32) {
	high := uint16(v >> 16)
	low := uint16(v)
	if order == OrderLowHigh {
		out[addr] = low
		out[addr+1] = high
		return
	}
	out[addr] = high
	out[addr+1] = low
}
