package modbus

import (
	"math"

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

func valueFor(snap model.TelemetrySnapshot, name string) (float64, bool) {
	switch name {
	case "soc_percent":
		return snap.SocPercent, true
	case "soh_percent":
		return snap.SohPercent, true
	case "active_power_kw":
		return snap.ActivePowerKw, true
	case "reactive_power_kvar":
		return snap.ReactivePowerKvar, true
	case "dc_voltage":
		return snap.DcVoltage, true
	case "dc_current":
		return snap.DcCurrent, true
	case "temperature_celsius":
		return snap.TemperatureCelsius, true
	case "available":
		if snap.Available {
			return 1, true
		}
		return 0, true
	case "fault_status":
		if snap.FaultStatus == "" || snap.FaultStatus == "ok" {
			return 0, true
		}
		return 1, true
	default:
		return 0, false
	}
}

// TelemetryRegisterNames returns the snapshot-backed register names the
// simulator can serve — the same set valueFor answers. Exported so the
// golden-vector conformance check scopes itself to sim-served registers
// without re-listing the names.
func TelemetryRegisterNames() []string {
	return []string{
		"soc_percent", "soh_percent", "active_power_kw", "reactive_power_kvar",
		"dc_voltage", "dc_current", "temperature_celsius", "available", "fault_status",
	}
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
