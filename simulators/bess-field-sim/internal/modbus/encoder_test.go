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
	if got.Holding[100] != 605 {
		t.Errorf("soc_percent at 100: want 605, got %d", got.Holding[100])
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
	if got.Holding[110] != want {
		t.Errorf("active_power_kw: want %d, got %d", want, got.Holding[110])
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
	high, hasHigh := got.Holding[200]
	low, hasLow := got.Holding[201]
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
	combined := uint32(got.Holding[300])<<16 | uint32(got.Holding[301])
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
	combined := uint32(got.Holding[400])<<16 | uint32(got.Holding[401])
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
	if on.Holding[120] != 1 {
		t.Errorf("available=true: want 1, got %d", on.Holding[120])
	}
	if off.Holding[120] != 0 {
		t.Errorf("available=false: want 0, got %d", off.Holding[120])
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
	if ok.Holding[122] != 0 {
		t.Errorf("fault_status=ok: want 0, got %d", ok.Holding[122])
	}
	if fault.Holding[122] != 1 {
		t.Errorf("fault_status=comm-loss: want 1, got %d", fault.Holding[122])
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
	if _, has := got.Holding[100]; !has {
		t.Error("expected soc_percent at 100")
	}
	if _, has := got.Holding[200]; has {
		t.Error("writable register must not be encoded")
	}
	if _, has := got.Holding[300]; has {
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
	if _, has := got.Holding[100]; has {
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
	if got.Holding[100] != 42 {
		t.Errorf("scale=0 passthrough: want 42, got %d", got.Holding[100])
	}
}

func TestEncodeSnapshot_Float32LowHighSwapsWords(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "active_power_kw", Address: 1, Type: "float32", ScaleFactor: 1000, WordOrder: "low_high", RegisterTable: "input"},
		},
	}
	image := modbus.EncodeSnapshot(model.TelemetrySnapshot{ActivePowerKw: 62.5}, mapping)
	bits := math.Float32bits(0.0625)
	wantLow := uint16(bits)
	wantHigh := uint16(bits >> 16)
	if image.Input[1] != wantLow || image.Input[2] != wantHigh {
		t.Errorf("low_high float32: want [%d %d], got [%d %d]", wantLow, wantHigh, image.Input[1], image.Input[2])
	}
	if _, leaked := image.Holding[1]; leaked {
		t.Error("input register must not appear in the holding image")
	}
}

func TestEncodeSnapshot_DefaultsAreHoldingHighLow(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "active_power_kw", Address: 200, Type: "int32", ScaleFactor: 1},
		},
	}
	image := modbus.EncodeSnapshot(model.TelemetrySnapshot{ActivePowerKw: -100000}, mapping)
	combined := uint32(image.Holding[200])<<16 | uint32(image.Holding[201])
	if int32(combined) != -100000 {
		t.Errorf("default high_low/holding: want -100000, got %d", int32(combined))
	}
}

// Second-review finding 1: TelemetryRegisterNames, valueFor and
// isTelemetryRegister must stay one set. Building a mapping from the
// exported list and encoding a fully populated snapshot exercises every
// accessor: a name the encoder cannot source would stay at word 0 here.
func TestTelemetryRegisterNames_EveryNameEncodes(t *testing.T) {
	t.Parallel()

	snap := model.TelemetrySnapshot{
		SocPercent: 1, SohPercent: 2, ActivePowerKw: 3, ReactivePowerKvar: 4,
		DcVoltage: 5, DcCurrent: 6, TemperatureCelsius: 7,
		Available: true, FaultStatus: "overtemperature",
	}
	want := map[string]uint16{
		"soc_percent": 1, "soh_percent": 2, "active_power_kw": 3,
		"reactive_power_kvar": 4, "dc_voltage": 5, "dc_current": 6,
		"temperature_celsius": 7, "available": 1, "fault_status": 1,
	}

	names := modbus.TelemetryRegisterNames()
	if len(names) != len(want) {
		t.Fatalf("TelemetryRegisterNames: want %d names, got %d (%v)", len(want), len(names), names)
	}
	registers := make([]model.ModbusRegister, 0, len(names))
	for i, name := range names {
		registers = append(registers, model.ModbusRegister{Name: name, Address: i * 2, Type: "uint16", ScaleFactor: 1})
	}
	mapping := model.ModbusMapping{ProfileName: "sync", UnitIDDiscovery: "none", Registers: registers}
	if err := modbus.ValidateMapping(mapping); err != nil {
		t.Fatalf("every exported name must pass isTelemetryRegister: %v", err)
	}

	image := modbus.EncodeSnapshot(snap, mapping)
	for i, name := range names {
		got, ok := image.Holding[i*2]
		if !ok {
			t.Errorf("name %q: valueFor answered nothing — name lists drifted", name)
			continue
		}
		if got != want[name] {
			t.Errorf("name %q: want word %d, got %d", name, want[name], got)
		}
	}

	// Zero-value snapshot covers the false/ok accessor branches.
	zero := modbus.EncodeSnapshot(model.TelemetrySnapshot{FaultStatus: "ok"}, mapping)
	availableIdx := -1
	faultIdx := -1
	for i, name := range names {
		if name == "available" {
			availableIdx = i * 2
		}
		if name == "fault_status" {
			faultIdx = i * 2
		}
	}
	if zero.Holding[availableIdx] != 0 || zero.Holding[faultIdx] != 0 {
		t.Errorf("zero snapshot: available/fault words want 0/0, got %d/%d", zero.Holding[availableIdx], zero.Holding[faultIdx])
	}
}
