package modbus_test

import (
	"errors"
	"path/filepath"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/modbus"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/testroot"
)

func TestMain(m *testing.M) {
	testroot.Main(m)
}

func TestLoadMapping_VendorNeutralExample(t *testing.T) {
	t.Parallel()

	m, err := modbus.LoadMapping(repoMapping(t, "modbus.simulator.json"))
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if m.ProfileName != "vendor-neutral-simulator" {
		t.Errorf("profile_name: got %q", m.ProfileName)
	}
	if m.UnitIDDiscovery != "static" || m.StaticUnitID == nil || *m.StaticUnitID != 1 {
		t.Errorf("expected static unit id 1, got %v / %v", m.UnitIDDiscovery, m.StaticUnitID)
	}
	if len(m.Registers) == 0 {
		t.Fatal("registers empty")
	}
}

func TestLoadMapping_NotFound(t *testing.T) {
	t.Parallel()

	_, err := modbus.LoadMapping("nonexistent/mapping.json")
	if err == nil {
		t.Fatal("expected error for missing file")
	}
}

func TestLoadMapping_RejectsUnsafePath(t *testing.T) {
	t.Parallel()

	for _, path := range []string{"/nonexistent/mapping.json", "../mapping.json"} {
		_, err := modbus.LoadMapping(path)
		if err == nil {
			t.Fatalf("expected error for unsafe path %q", path)
		}
	}
}

func TestParseMapping_RejectsMissingProfile(t *testing.T) {
	t.Parallel()

	_, err := modbus.ParseMapping([]byte(`{"unit_id_discovery":"static","static_unit_id":1,"registers":[{"name":"x","address":0,"type":"uint16","scale_factor":1,"range":[0,1],"writable":false}]}`))
	if !errors.Is(err, modbus.ErrMappingMissingProfile) {
		t.Fatalf("expected ErrMappingMissingProfile, got %v", err)
	}
}

func TestParseMapping_RejectsMissingDiscovery(t *testing.T) {
	t.Parallel()

	_, err := modbus.ParseMapping([]byte(`{"profile_name":"p","registers":[{"name":"x","address":0,"type":"uint16","scale_factor":1,"range":[0,1],"writable":false}]}`))
	if !errors.Is(err, modbus.ErrMappingMissingDiscovery) {
		t.Fatalf("expected ErrMappingMissingDiscovery, got %v", err)
	}
}

func TestParseMapping_RejectsEmptyRegisters(t *testing.T) {
	t.Parallel()

	_, err := modbus.ParseMapping([]byte(`{"profile_name":"p","unit_id_discovery":"static","static_unit_id":1,"registers":[]}`))
	if !errors.Is(err, modbus.ErrMappingNoRegisters) {
		t.Fatalf("expected ErrMappingNoRegisters, got %v", err)
	}
}

func TestParseMapping_RejectsStaticWithoutUnitID(t *testing.T) {
	t.Parallel()

	_, err := modbus.ParseMapping([]byte(`{"profile_name":"p","unit_id_discovery":"static","registers":[{"name":"x","address":0,"type":"uint16","scale_factor":1,"range":[0,1],"writable":false}]}`))
	if !errors.Is(err, modbus.ErrStaticDiscoveryWithoutUnitID) {
		t.Fatalf("expected ErrStaticDiscoveryWithoutUnitID, got %v", err)
	}
}

func TestParseMapping_MalformedJSON(t *testing.T) {
	t.Parallel()

	_, err := modbus.ParseMapping([]byte("not json"))
	if err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateMapping_AcceptsSunspecDiscoveryWithoutUnitID(t *testing.T) {
	t.Parallel()

	m := model.ModbusMapping{
		ProfileName:     "sunspec",
		UnitIDDiscovery: "sunspec",
		Registers:       []model.ModbusRegister{{Name: "soc_percent", Address: 0, Type: "uint16"}},
	}
	if err := modbus.ValidateMapping(m); err != nil {
		t.Fatalf("expected nil, got %v", err)
	}
}

func TestValidateMapping_RejectsUnknownReadOnlyRegister(t *testing.T) {
	t.Parallel()

	m := model.ModbusMapping{
		ProfileName:     "p",
		UnitIDDiscovery: "sunspec",
		Registers:       []model.ModbusRegister{{Name: "soc_pct", Address: 0, Type: "uint16"}},
	}
	if !errors.Is(modbus.ValidateMapping(m), modbus.ErrMappingUnknownRegisterName) {
		t.Fatal("expected ErrMappingUnknownRegisterName")
	}
}

func TestValidateMapping_RejectsUnsupportedRegisterType(t *testing.T) {
	t.Parallel()

	m := model.ModbusMapping{
		ProfileName:     "p",
		UnitIDDiscovery: "sunspec",
		Registers:       []model.ModbusRegister{{Name: "soc_percent", Address: 0, Type: "string"}},
	}
	if !errors.Is(modbus.ValidateMapping(m), modbus.ErrMappingUnsupportedRegisterType) {
		t.Fatal("expected ErrMappingUnsupportedRegisterType")
	}
}

func TestValidateMapping_RejectsMultiWordRegisterPastAddressSpace(t *testing.T) {
	t.Parallel()

	m := model.ModbusMapping{
		ProfileName:     "p",
		UnitIDDiscovery: "sunspec",
		Registers:       []model.ModbusRegister{{Name: "soc_percent", Address: 65535, Type: "uint32"}},
	}
	if !errors.Is(modbus.ValidateMapping(m), modbus.ErrMappingRegisterOutOfRange) {
		t.Fatal("expected ErrMappingRegisterOutOfRange")
	}
}

func TestValidateMapping_RejectsNegativeRegisterAddress(t *testing.T) {
	t.Parallel()

	m := model.ModbusMapping{
		ProfileName:     "p",
		UnitIDDiscovery: "sunspec",
		Registers:       []model.ModbusRegister{{Name: "soc_percent", Address: -1, Type: "uint16"}},
	}
	if !errors.Is(modbus.ValidateMapping(m), modbus.ErrMappingRegisterOutOfRange) {
		t.Fatal("expected ErrMappingRegisterOutOfRange")
	}
}

func TestLoadMapping_MalformedJSONOnDisk(t *testing.T) {
	t.Parallel()

	path := filepath.Join("testdata", "malformed", "modbus-mapping.invalid-json")
	_, err := modbus.LoadMapping(path)
	if err == nil {
		t.Fatal("expected error")
	}
}

func repoMapping(t *testing.T, name string) string {
	t.Helper()
	return filepath.Join("testdata", "mappings", name)
}

func TestValidateMapping_RejectsInvalidRegisterTableAndWordOrder(t *testing.T) {
	t.Parallel()

	base := model.ModbusMapping{
		ProfileName:     "p",
		UnitIDDiscovery: "none",
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 100, Type: "uint16", RegisterTable: "coil"},
		},
	}
	if err := modbus.ValidateMapping(base); !errors.Is(err, modbus.ErrMappingInvalidRegisterTable) {
		t.Errorf("want ErrMappingInvalidRegisterTable, got %v", err)
	}

	base.Registers[0] = model.ModbusRegister{Name: "soc_percent", Address: 100, Type: "float32", WordOrder: "middle_endian"}
	if err := modbus.ValidateMapping(base); !errors.Is(err, modbus.ErrMappingInvalidWordOrder) {
		t.Errorf("want ErrMappingInvalidWordOrder, got %v", err)
	}
}
