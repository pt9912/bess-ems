package modbus

import (
	"encoding/binary"
	"sync"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/tbrandon/mbserver"
)

const maxReadHoldingRegisters = 125

// Server is the simulator's Modbus TCP endpoint. It exposes the
// configured mapping over TCP and refreshes the holding-register space
// from the latest TelemetrySnapshot whenever Apply is called.
type Server struct {
	mapping model.ModbusMapping
	mb      *mbserver.Server
	mu      sync.RWMutex
	regs    []uint16
}

// NewServer returns a Server for the given mapping. The TCP listener is
// not started until ListenTCP is called.
func NewServer(mapping model.ModbusMapping) *Server {
	mb := mbserver.NewServer()
	s := &Server{
		mapping: mapping,
		mb:      mb,
		regs:    make([]uint16, holdingRegisterCount),
	}
	mb.RegisterFunctionHandler(3, s.readHoldingRegisters)
	return s
}

// ListenTCP binds the underlying mbserver instance to addr.
func (s *Server) ListenTCP(addr string) error {
	if err := s.mb.ListenTCP(addr); err != nil {
		return err
	}
	return nil
}

// Close stops the TCP listener and frees mbserver resources.
func (s *Server) Close() {
	s.mb.Close()
}

// Apply refreshes the holding-register space from the supplied snapshot. Reads
// are served by a custom mbserver function handler over Server.regs, because
// mbserver's exported HoldingRegisters slice is otherwise read by its request
// goroutine without synchronizing with simulator writes.
// Registers outside the address window are silently ignored.
func (s *Server) Apply(snap model.TelemetrySnapshot) {
	regs := EncodeSnapshot(snap, s.mapping)
	s.mu.Lock()
	defer s.mu.Unlock()
	for addr, word := range regs {
		if addr < 0 || addr >= len(s.regs) {
			continue
		}
		s.regs[addr] = word
	}
}

func (s *Server) readHoldingRegisters(_ *mbserver.Server, frame mbserver.Framer) ([]byte, *mbserver.Exception) {
	data := frame.GetData()
	if len(data) < 4 {
		return []byte{}, &mbserver.IllegalDataValue
	}

	register := int(binary.BigEndian.Uint16(data[0:2]))
	numRegs := int(binary.BigEndian.Uint16(data[2:4]))
	endRegister := register + numRegs
	if numRegs < 1 || numRegs > maxReadHoldingRegisters || endRegister < register || endRegister > len(s.regs) {
		return []byte{}, &mbserver.IllegalDataAddress
	}

	s.mu.RLock()
	defer s.mu.RUnlock()
	return append([]byte{byte(numRegs * 2)}, mbserver.Uint16ToBytes(s.regs[register:endRegister])...), &mbserver.Success
}
