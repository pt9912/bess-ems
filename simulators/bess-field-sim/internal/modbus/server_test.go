package modbus_test

import (
	"context"
	"net"
	"strconv"
	"testing"
	"time"

	gridx "github.com/grid-x/modbus"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/modbus"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

func TestServer_RoundtripSocAndPower(t *testing.T) {
	t.Parallel()

	addr := freeAddr(t)
	staticID := 1
	mapping := model.ModbusMapping{
		ProfileName:     "test",
		UnitIDDiscovery: "static",
		StaticUnitID:    &staticID,
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 100, Type: "uint16", ScaleFactor: 0.1},
			{Name: "active_power_kw", Address: 110, Type: "int16", ScaleFactor: 0.1},
			{Name: "available", Address: 120, Type: "uint16", ScaleFactor: 1},
		},
	}

	srv := modbus.NewServer(mapping)
	if err := srv.ListenTCP(addr); err != nil {
		t.Fatalf("listen: %v", err)
	}
	defer srv.Close()

	srv.Apply(model.TelemetrySnapshot{SocPercent: 60.5, ActivePowerKw: -25, Available: true})
	time.Sleep(50 * time.Millisecond)

	ctx := context.Background()
	h := gridx.NewTCPClientHandler(addr)
	h.SetSlave(byte(staticID))
	if err := h.Connect(ctx); err != nil {
		t.Fatalf("connect: %v", err)
	}
	defer func() { _ = h.Close() }()
	c := gridx.NewClient(h)

	socBytes, err := c.ReadHoldingRegisters(ctx, 100, 1)
	if err != nil {
		t.Fatalf("read soc: %v", err)
	}
	if got := uint16(socBytes[0])<<8 | uint16(socBytes[1]); got != 605 {
		t.Errorf("soc: want 605, got %d", got)
	}

	powerBytes, err := c.ReadHoldingRegisters(ctx, 110, 1)
	if err != nil {
		t.Fatalf("read power: %v", err)
	}
	if got := int16(uint16(powerBytes[0])<<8 | uint16(powerBytes[1])); got != -250 {
		t.Errorf("power: want -250, got %d", got)
	}

	availBytes, err := c.ReadHoldingRegisters(ctx, 120, 1)
	if err != nil {
		t.Fatalf("read available: %v", err)
	}
	if got := uint16(availBytes[0])<<8 | uint16(availBytes[1]); got != 1 {
		t.Errorf("available: want 1, got %d", got)
	}
}

func TestServer_ApplyRefreshesValues(t *testing.T) {
	t.Parallel()

	addr := freeAddr(t)
	staticID := 1
	mapping := model.ModbusMapping{
		ProfileName:     "test",
		UnitIDDiscovery: "static",
		StaticUnitID:    &staticID,
		Registers: []model.ModbusRegister{
			{Name: "soc_percent", Address: 100, Type: "uint16", ScaleFactor: 0.1},
		},
	}

	srv := modbus.NewServer(mapping)
	if err := srv.ListenTCP(addr); err != nil {
		t.Fatalf("listen: %v", err)
	}
	defer srv.Close()

	srv.Apply(model.TelemetrySnapshot{SocPercent: 50.0})
	time.Sleep(20 * time.Millisecond)
	srv.Apply(model.TelemetrySnapshot{SocPercent: 75.5})
	time.Sleep(20 * time.Millisecond)

	ctx := context.Background()
	h := gridx.NewTCPClientHandler(addr)
	h.SetSlave(byte(staticID))
	if err := h.Connect(ctx); err != nil {
		t.Fatalf("connect: %v", err)
	}
	defer func() { _ = h.Close() }()

	bytes, err := gridx.NewClient(h).ReadHoldingRegisters(ctx, 100, 1)
	if err != nil {
		t.Fatalf("read: %v", err)
	}
	if got := uint16(bytes[0])<<8 | uint16(bytes[1]); got != 755 {
		t.Errorf("soc after refresh: want 755, got %d", got)
	}
}

func freeAddr(t *testing.T) string {
	t.Helper()
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	port := l.Addr().(*net.TCPAddr).Port
	if err := l.Close(); err != nil {
		t.Fatalf("close: %v", err)
	}
	return "127.0.0.1:" + strconv.Itoa(port)
}
