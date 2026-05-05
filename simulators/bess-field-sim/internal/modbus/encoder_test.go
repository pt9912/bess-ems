package modbus_test

import (
	"math"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/modbus"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

func TestEncodeSnapshot_Uint16WithScale(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		ProfileName:     "p",
		UnitIDDiscovery: "static",
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 100, Type: "uint16", ScaleFactor: 0.1},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{SocPercent: 60.5}, mapping)
	if got[100] != 605 {
		t.Errorf("soc_percent at 100: want 605, got %d", got[100])
	}
}

func TestEncodeSnapshot_Int16Negative(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "active_power_kw", Address: 110, Type: "int16", ScaleFactor: 0.1},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{ActivePowerKw: -25}, mapping)
	signed := int16(-250)
	want := uint16(signed)
	if got[110] != want {
		t.Errorf("active_power_kw: want %d, got %d", want, got[110])
	}
}

func TestEncodeSnapshot_Int32MultiWord(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "active_power_kw", Address: 200, Type: "int32", ScaleFactor: 1},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{ActivePowerKw: -100000}, mapping)
	high, hasHigh := got[200]
	low, hasLow := got[201]
	if !hasHigh || !hasLow {
		t.Fatalf("expected words at 200 and 201, got %v", got)
	}
	combined := uint32(high)<<16 | uint32(low)
	if int32(combined) != -100000 {
		t.Errorf("decoded int32: want -100000, got %d", int32(combined))
	}
}

func TestEncodeSnapshot_Uint32MultiWord(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 300, Type: "uint32", ScaleFactor: 1},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{SocPercent: 70000}, mapping)
	combined := uint32(got[300])<<16 | uint32(got[301])
	if combined != 70000 {
		t.Errorf("uint32 decode: want 70000, got %d", combined)
	}
}

func TestEncodeSnapshot_Float32MultiWord(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "temperature_celsius", Address: 400, Type: "float32", ScaleFactor: 1},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{TemperatureCelsius: 22.5}, mapping)
	combined := uint32(got[400])<<16 | uint32(got[401])
	decoded := math.Float32frombits(combined)
	if decoded != 22.5 {
		t.Errorf("float32 decode: want 22.5, got %g", decoded)
	}
}

func TestEncodeSnapshot_AvailableBoolean(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "available", Address: 120, Type: "uint16", ScaleFactor: 1},
		},
	}
	on := modbus.EncodeSnapshot(model.TelemetrySnapshot{Available: true}, mapping)
	off := modbus.EncodeSnapshot(model.TelemetrySnapshot{Available: false}, mapping)
	if on[120] != 1 {
		t.Errorf("available=true: want 1, got %d", on[120])
	}
	if off[120] != 0 {
		t.Errorf("available=false: want 0, got %d", off[120])
	}
}

func TestEncodeSnapshot_FaultStatus(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "fault_status", Address: 122, Type: "uint16", ScaleFactor: 1},
		},
	}
	ok := modbus.EncodeSnapshot(model.TelemetrySnapshot{FaultStatus: "ok"}, mapping)
	fault := modbus.EncodeSnapshot(model.TelemetrySnapshot{FaultStatus: "comm-loss"}, mapping)
	if ok[122] != 0 {
		t.Errorf("fault_status=ok: want 0, got %d", ok[122])
	}
	if fault[122] != 1 {
		t.Errorf("fault_status=comm-loss: want 1, got %d", fault[122])
	}
}

func TestEncodeSnapshot_SkipsWritableAndUnknownNames(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 100, Type: "uint16", ScaleFactor: 1, Writable: false},
			{Name: "active_power_setpoint_kw", Address: 200, Type: "int16", Writable: true},
			{Name: "completely_unknown_field", Address: 300, Type: "uint16"},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{SocPercent: 50}, mapping)
	if _, has := got[100]; !has {
		t.Error("expected soc_percent at 100")
	}
	if _, has := got[200]; has {
		t.Error("writable register must not be encoded")
	}
	if _, has := got[300]; has {
		t.Error("unknown field must not be encoded")
	}
}

func TestEncodeSnapshot_SkipsUnsupportedType(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 100, Type: "string", ScaleFactor: 1},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{SocPercent: 50}, mapping)
	if _, has := got[100]; has {
		t.Error("unsupported register type must not be encoded")
	}
}

func TestEncodeSnapshot_ZeroScaleFactorPassesThrough(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 100, Type: "uint16", ScaleFactor: 0},
		},
	}
	got := modbus.EncodeSnapshot(model.TelemetrySnapshot{SocPercent: 42}, mapping)
	if got[100] != 42 {
		t.Errorf("scale=0 passthrough: want 42, got %d", got[100])
	}
}
