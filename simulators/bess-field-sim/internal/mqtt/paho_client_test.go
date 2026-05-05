package mqtt_test

import (
	"context"
	"net"
	"strconv"
	"sync"
	"testing"
	"time"

	mqttserver "github.com/mochi-mqtt/server/v2"
	"github.com/mochi-mqtt/server/v2/hooks/auth"
	"github.com/mochi-mqtt/server/v2/listeners"
	"github.com/mochi-mqtt/server/v2/packets"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/mqtt"
)

type capturedPublish struct {
	topic   string
	payload string
}

type captureHook struct {
	mqttserver.HookBase
	mu       sync.Mutex
	captured []capturedPublish
}

func (h *captureHook) ID() string {
	return "capture"
}

func (h *captureHook) Provides(b byte) bool {
	return b == mqttserver.OnPublished
}

func (h *captureHook) OnPublished(_ *mqttserver.Client, pk packets.Packet) {
	h.mu.Lock()
	defer h.mu.Unlock()
	h.captured = append(h.captured, capturedPublish{topic: pk.TopicName, payload: string(pk.Payload)})
}

func (h *captureHook) snapshot() []capturedPublish {
	h.mu.Lock()
	defer h.mu.Unlock()
	out := make([]capturedPublish, len(h.captured))
	copy(out, h.captured)
	return out
}

func startBroker(t *testing.T) (string, *captureHook) {
	t.Helper()

	srv := mqttserver.New(nil)
	if err := srv.AddHook(new(auth.AllowHook), nil); err != nil {
		t.Fatalf("auth hook: %v", err)
	}
	hook := &captureHook{}
	if err := srv.AddHook(hook, nil); err != nil {
		t.Fatalf("capture hook: %v", err)
	}

	addr := freeMqttAddr(t)
	tcp := listeners.NewTCP(listeners.Config{ID: "tcp", Address: addr})
	if err := srv.AddListener(tcp); err != nil {
		t.Fatalf("listener: %v", err)
	}

	go func() {
		if err := srv.Serve(); err != nil {
			t.Logf("broker serve: %v", err)
		}
	}()
	t.Cleanup(func() { _ = srv.Close() })
	waitTCP(t, addr)
	return "tcp://" + addr, hook
}

func TestPahoClient_PublishReachesBroker(t *testing.T) {
	t.Parallel()

	url, hook := startBroker(t)

	c, err := mqtt.NewPahoClient(url, "publish-test")
	if err != nil {
		t.Fatalf("connect: %v", err)
	}
	defer c.Close()

	if err := c.Publish(context.Background(), "battery/x/telemetry", false, []byte(`{"soc":60}`)); err != nil {
		t.Fatalf("publish: %v", err)
	}

	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		if got := hook.snapshot(); len(got) > 0 {
			if got[0].topic != "battery/x/telemetry" {
				t.Errorf("topic: got %q", got[0].topic)
			}
			if got[0].payload != `{"soc":60}` {
				t.Errorf("payload: got %q", got[0].payload)
			}
			return
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatal("broker never received the publish")
}

func TestPahoClient_PublishRespectsContextCancellation(t *testing.T) {
	t.Parallel()

	url, _ := startBroker(t)
	c, err := mqtt.NewPahoClient(url, "cancel-test")
	if err != nil {
		t.Fatalf("connect: %v", err)
	}
	defer c.Close()

	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	err = c.Publish(ctx, "battery/x/telemetry", false, []byte(`x`))
	if err == nil {
		t.Fatal("expected error from cancelled context")
	}
}

func TestNewPahoClient_RejectsEmptyBroker(t *testing.T) {
	t.Parallel()

	_, err := mqtt.NewPahoClient("", "x")
	if err == nil {
		t.Fatal("expected error for empty broker URL")
	}
}

func TestNewPahoClient_FailsOnUnreachableBroker(t *testing.T) {
	t.Parallel()

	_, err := mqtt.NewPahoClient("tcp://127.0.0.1:1", "unreachable")
	if err == nil {
		t.Fatal("expected connect error")
	}
}

func freeMqttAddr(t *testing.T) string {
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
