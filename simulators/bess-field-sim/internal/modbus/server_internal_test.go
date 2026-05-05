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
