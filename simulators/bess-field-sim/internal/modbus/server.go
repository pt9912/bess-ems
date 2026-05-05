package modbus

import (
	"sync"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/tbrandon/mbserver"
)

// Server is the simulator's Modbus TCP endpoint. It exposes the
// configured mapping over TCP and refreshes the holding-register space
// from the latest TelemetrySnapshot whenever Apply is called.
type Server struct {
	mapping model.ModbusMapping
	mb      *mbserver.Server
	mu      sync.Mutex
}

// NewServer returns a Server for the given mapping. The TCP listener is
// not started until ListenTCP is called.
func NewServer(mapping model.ModbusMapping) *Server {
	return &Server{
		mapping: mapping,
		mb:      mbserver.NewServer(),
	}
}

// ListenTCP binds the underlying mbserver instance to addr.
func (s *Server) ListenTCP(addr string) error {
	if err := s.mb.ListenTCP(addr); err != nil {
		return err //nolint:wrapcheck // mbserver errors carry enough context
	}
	return nil
}

// Close stops the TCP listener and frees mbserver resources.
func (s *Server) Close() {
	s.mb.Close()
}

// Apply refreshes the holding-register space from the supplied snapshot.
// Registers outside the mbserver address window are silently ignored.
func (s *Server) Apply(snap model.TelemetrySnapshot) {
	regs := EncodeSnapshot(snap, s.mapping)
	s.mu.Lock()
	defer s.mu.Unlock()
	for addr, word := range regs {
		if addr < 0 || addr >= len(s.mb.HoldingRegisters) {
			continue
		}
		s.mb.HoldingRegisters[addr] = word
	}
}
