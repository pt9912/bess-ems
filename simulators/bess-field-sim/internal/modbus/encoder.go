package modbus

import (
	"math"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// EncodeSnapshot returns address→word entries for the read-only registers
// listed in the mapping that have a corresponding TelemetrySnapshot field.
// Multi-word types occupy consecutive addresses with the high word first
// per Modbus convention.
func EncodeSnapshot(snap model.TelemetrySnapshot, mapping model.ModbusMapping) map[int]uint16 {
	out := make(map[int]uint16, len(mapping.Registers))
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
		_ = writeRegister(out, reg.Address, reg.Type, raw)
	}
	return out
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

func writeRegister(out map[int]uint16, addr int, typ string, raw float64) bool {
	switch typ {
	case "uint16":
		out[addr] = uint16(raw)
	case "int16":
		out[addr] = uint16(int16(raw))
	case "uint32":
		v := uint32(raw)
		out[addr] = uint16(v >> 16)
		out[addr+1] = uint16(v)
	case "int32":
		v := uint32(int32(raw))
		out[addr] = uint16(v >> 16)
		out[addr+1] = uint16(v)
	case "float32":
		v := math.Float32bits(float32(raw))
		out[addr] = uint16(v >> 16)
		out[addr+1] = uint16(v)
	default:
		return false
	}
	return true
}
