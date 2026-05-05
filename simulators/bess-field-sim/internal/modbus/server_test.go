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
	waitTCP(t, addr)

	srv.Apply(model.TelemetrySnapshot{SocPercent: 60.5, ActivePowerKw: -25, Available: true})

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
	waitTCP(t, addr)

	srv.Apply(model.TelemetrySnapshot{SocPercent: 50.0})
	srv.Apply(model.TelemetrySnapshot{SocPercent: 75.5})

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

func TestServer_ReadHoldingRegistersRejectsProtocolOversize(t *testing.T) {
	t.Parallel()

	addr := freeAddr(t)
	staticID := 1
	srv := modbus.NewServer(model.ModbusMapping{
		ProfileName:     "test",
		UnitIDDiscovery: "static",
		StaticUnitID:    &staticID,
	})
	if err := srv.ListenTCP(addr); err != nil {
		t.Fatalf("listen: %v", err)
	}
	defer srv.Close()
	waitTCP(t, addr)

	ctx := context.Background()
	h := gridx.NewTCPClientHandler(addr)
	h.SetSlave(byte(staticID))
	if err := h.Connect(ctx); err != nil {
		t.Fatalf("connect: %v", err)
	}
	defer func() { _ = h.Close() }()

	if _, err := gridx.NewClient(h).ReadHoldingRegisters(ctx, 0, 126); err == nil {
		t.Fatal("expected oversized FC3 read to fail")
	}
}

func TestServer_WriteSingleRegisterRoundtrip(t *testing.T) {
	t.Parallel()

	withTestClient(t, freeAddr(t), 1, func(client gridx.Client) {
		ctx := context.Background()

		if _, err := client.WriteSingleRegister(ctx, 200, 1234); err != nil {
			t.Fatalf("write single register: %v", err)
		}

		bytes, err := client.ReadHoldingRegisters(ctx, 200, 1)
		if err != nil {
			t.Fatalf("read written register: %v", err)
		}
		if got := uint16(bytes[0])<<8 | uint16(bytes[1]); got != 1234 {
			t.Errorf("written register: want 1234, got %d", got)
		}
	})
}

func TestServer_WriteMultipleRegistersRoundtrip(t *testing.T) {
	t.Parallel()

	withTestClient(t, freeAddr(t), 1, func(client gridx.Client) {
		ctx := context.Background()

		if _, err := client.WriteMultipleRegisters(ctx, 202, 2, []byte{0x12, 0x34, 0xab, 0xcd}); err != nil {
			t.Fatalf("write multiple registers: %v", err)
		}

		bytes, err := client.ReadHoldingRegisters(ctx, 202, 2)
		if err != nil {
			t.Fatalf("read written registers: %v", err)
		}
		gotA := uint16(bytes[0])<<8 | uint16(bytes[1])
		gotB := uint16(bytes[2])<<8 | uint16(bytes[3])
		if gotA != 0x1234 || gotB != 0xabcd {
			t.Errorf("written registers: want 0x1234/0xabcd, got %#04x/%#04x", gotA, gotB)
		}
	})
}

func TestServer_WriteMultipleRegistersRejectsProtocolOversize(t *testing.T) {
	t.Parallel()

	withTestClient(t, freeAddr(t), 1, func(client gridx.Client) {
		_, err := client.WriteMultipleRegisters(context.Background(), 0, 124, make([]byte, 248))
		if err == nil {
			t.Fatal("expected oversized FC16 write to fail")
		}
	})
}

func TestServer_WriteMultipleRegistersRejectsOutOfRange(t *testing.T) {
	t.Parallel()

	withTestClient(t, freeAddr(t), 1, func(client gridx.Client) {
		_, err := client.WriteMultipleRegisters(context.Background(), 65535, 2, []byte{0, 1, 0, 2})
		if err == nil {
			t.Fatal("expected out-of-range FC16 write to fail")
		}
	})
}

func withTestClient(t *testing.T, addr string, unitID int, run func(gridx.Client)) {
	t.Helper()
	srv := modbus.NewServer(model.ModbusMapping{
		ProfileName:     "test",
		UnitIDDiscovery: "static",
		StaticUnitID:    &unitID,
	})
	if err := srv.ListenTCP(addr); err != nil {
		t.Fatalf("listen: %v", err)
	}
	t.Cleanup(srv.Close)
	waitTCP(t, addr)

	h := gridx.NewTCPClientHandler(addr)
	h.SetSlave(byte(unitID))
	if err := h.Connect(context.Background()); err != nil {
		t.Fatalf("connect: %v", err)
	}
	t.Cleanup(func() { _ = h.Close() })
	run(gridx.NewClient(h))
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

func waitTCP(t *testing.T, addr string) {
	t.Helper()
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		conn, err := net.DialTimeout("tcp", addr, 20*time.Millisecond)
		if err == nil {
			_ = conn.Close()
			return
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatalf("tcp listener %s did not become ready", addr)
}
