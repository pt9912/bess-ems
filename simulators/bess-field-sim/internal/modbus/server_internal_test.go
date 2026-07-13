package modbus

import (
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/tbrandon/mbserver"
)

func TestServer_WriteSingleRegisterRejectsShortFrame(t *testing.T) {
	t.Parallel()

	_, exception := NewServer(model.ModbusMapping{}).writeSingleRegister(nil, tcpFrame(6, []byte{0, 1, 2}))
	if exception != &mbserver.IllegalDataValue {
		t.Fatalf("expected IllegalDataValue, got %v", exception)
	}
}

func TestServer_WriteMultipleRegistersRejectsShortFrame(t *testing.T) {
	t.Parallel()

	_, exception := NewServer(model.ModbusMapping{}).writeMultipleRegisters(nil, tcpFrame(16, []byte{0, 1, 0, 1}))
	if exception != &mbserver.IllegalDataValue {
		t.Fatalf("expected IllegalDataValue, got %v", exception)
	}
}

func TestServer_WriteMultipleRegistersRejectsByteCountMismatch(t *testing.T) {
	t.Parallel()

	_, exception := NewServer(model.ModbusMapping{}).writeMultipleRegisters(nil, tcpFrame(16, []byte{0, 1, 0, 2, 2, 0, 1}))
	if exception != &mbserver.IllegalDataValue {
		t.Fatalf("expected IllegalDataValue, got %v", exception)
	}
}

func tcpFrame(function uint8, data []byte) *mbserver.TCPFrame {
	return &mbserver.TCPFrame{Function: function, Data: data}
}

// ADR 0013 §5.4 (review finding 1): the serving half of the proven drift —
// input registers must be served via FC04 from their OWN space, holding via
// FC03, and writes must never leak into the input space. The mapping mirrors
// the HIL profile's address collision (input@1 AND holding@1).
func TestServer_ServesInputAndHoldingSpacesIndependently(t *testing.T) {
	t.Parallel()

	mapping := model.ModbusMapping{
		Registers: []model.ModbusRegister{
			{Name: "active_power_kw", Address: 1, Type: "float32", ScaleFactor: 1000, RegisterTable: TableInput, WordOrder: OrderLowHigh},
			{Name: "soc_percent", Address: 1, Type: "uint16", ScaleFactor: 1, RegisterTable: TableHolding},
		},
	}
	s := NewServer(mapping)
	s.Apply(model.TelemetrySnapshot{ActivePowerKw: 62.5, SocPercent: 60})

	image := EncodeSnapshot(model.TelemetrySnapshot{ActivePowerKw: 62.5, SocPercent: 60}, mapping)
	wantLow, wantHigh := image.Input[1], image.Input[2]

	inputResp, exception := s.readInputRegisters(nil, tcpFrame(4, []byte{0, 1, 0, 2}))
	if exception != &mbserver.Success {
		t.Fatalf("FC04: expected success, got %v", exception)
	}
	gotLow := uint16(inputResp[1])<<8 | uint16(inputResp[2])
	gotHigh := uint16(inputResp[3])<<8 | uint16(inputResp[4])
	if gotLow != wantLow || gotHigh != wantHigh {
		t.Fatalf("FC04 input words: want [%d %d], got [%d %d]", wantLow, wantHigh, gotLow, gotHigh)
	}

	holdingResp, exception := s.readHoldingRegisters(nil, tcpFrame(3, []byte{0, 1, 0, 1}))
	if exception != &mbserver.Success {
		t.Fatalf("FC03: expected success, got %v", exception)
	}
	gotHolding := uint16(holdingResp[1])<<8 | uint16(holdingResp[2])
	if gotHolding != 60 {
		t.Fatalf("FC03 holding word: want 60, got %d", gotHolding)
	}
	if gotHolding == gotLow && gotHolding == gotHigh {
		t.Fatal("holding read returned the input words — spaces are not independent")
	}

	// FC06 write to holding address 1 must not leak into the input space.
	if _, exception := s.writeSingleRegister(nil, tcpFrame(6, []byte{0, 1, 0x12, 0x34})); exception != &mbserver.Success {
		t.Fatalf("FC06: expected success, got %v", exception)
	}
	afterWrite, exception := s.readInputRegisters(nil, tcpFrame(4, []byte{0, 1, 0, 2}))
	if exception != &mbserver.Success {
		t.Fatalf("FC04 after write: expected success, got %v", exception)
	}
	if uint16(afterWrite[1])<<8|uint16(afterWrite[2]) != wantLow {
		t.Fatal("FC06 holding write leaked into the input space")
	}
	holdingAfter, _ := s.readHoldingRegisters(nil, tcpFrame(3, []byte{0, 1, 0, 1}))
	if uint16(holdingAfter[1])<<8|uint16(holdingAfter[2]) != 0x1234 {
		t.Fatal("FC06 write did not land in the holding space")
	}
}
