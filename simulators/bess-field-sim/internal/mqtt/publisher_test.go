package mqtt_test

import (
	"context"
	"errors"
	"sync"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/mqtt"
)

type fakeClient struct {
	mu              sync.Mutex
	messages        []capturedMessage
	subscriptions   []capturedSubscription
	failOn          string
	subscribeReturn error
}

type capturedMessage struct {
	topic    string
	retained bool
	payload  []byte
}

type capturedSubscription struct {
	topic   string
	handler mqtt.MessageHandler
}

func (f *fakeClient) Publish(_ context.Context, topic string, retained bool, payload []byte) error {
	f.mu.Lock()
	defer f.mu.Unlock()
	if f.failOn != "" && topic == f.failOn {
		return errors.New("fake publish failure")
	}
	cp := make([]byte, len(payload))
	copy(cp, payload)
	f.messages = append(f.messages, capturedMessage{topic: topic, retained: retained, payload: cp})
	return nil
}

func (f *fakeClient) Subscribe(_ context.Context, topic string, handler mqtt.MessageHandler) error {
	f.mu.Lock()
	defer f.mu.Unlock()
	if f.subscribeReturn != nil {
		return f.subscribeReturn
	}
	f.subscriptions = append(f.subscriptions, capturedSubscription{topic: topic, handler: handler})
	return nil
}

// deliver invokes the most-recent captured handler for topic with payload.
// Returns false if the test never registered a subscription for topic.
func (f *fakeClient) deliver(topic string, payload []byte) bool {
	f.mu.Lock()
	var handler mqtt.MessageHandler
	for i := len(f.subscriptions) - 1; i >= 0; i-- {
		if f.subscriptions[i].topic == topic {
			handler = f.subscriptions[i].handler
			break
		}
	}
	f.mu.Unlock()
	if handler == nil {
		return false
	}
	handler(topic, payload)
	return true
}

// capturedMessages returns a defensive copy of every published message.
func (f *fakeClient) capturedMessages() []capturedMessage {
	f.mu.Lock()
	defer f.mu.Unlock()
	out := make([]capturedMessage, len(f.messages))
	copy(out, f.messages)
	return out
}

func TestPublisher_PublishSnapshot_PublishesEmsSubscribeTopics(t *testing.T) {
	t.Parallel()

	// direction is EMS-perspective: simulator publishes what the EMS subscribes
	// to (telemetry, status) and skips what the EMS publishes (command).
	m := model.MqttMapping{
		ProfileName: "p",
		Topics: []model.MqttTopic{
			{Name: "telemetry", Topic: "battery/{assetId}/telemetry", Direction: "subscribe", Retained: false},
			{Name: "status", Topic: "battery/{assetId}/status", Direction: "subscribe", Retained: true},
			{Name: "command", Topic: "battery/{assetId}/command", Direction: "publish"},
		},
	}
	client := &fakeClient{}
	pub := mqtt.NewPublisher(client, "single-bess-1", m)

	if err := pub.PublishSnapshot(context.Background(), model.TelemetrySnapshot{SocPercent: 60, Available: true, FaultStatus: "ok"}); err != nil {
		t.Fatalf("publish: %v", err)
	}

	if len(client.messages) != 2 {
		t.Fatalf("expected 2 messages (telemetry+status), got %d", len(client.messages))
	}
	gotTopics := map[string]bool{}
	for _, msg := range client.messages {
		gotTopics[msg.topic] = msg.retained
	}
	if _, ok := gotTopics["battery/single-bess-1/telemetry"]; !ok {
		t.Error("missing telemetry publish")
	}
	if retained, ok := gotTopics["battery/single-bess-1/status"]; !ok || !retained {
		t.Error("status publish must be retained per mapping")
	}
}

func TestPublisher_PublishSnapshot_SurfacesClientError(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "telemetry", Topic: "battery/x/telemetry", Direction: "subscribe"},
		},
	}
	client := &fakeClient{failOn: "battery/x/telemetry"}
	pub := mqtt.NewPublisher(client, "x", m)

	err := pub.PublishSnapshot(context.Background(), model.TelemetrySnapshot{})
	if err == nil {
		t.Fatal("expected error from client")
	}
}

func TestPublisher_PublishSnapshot_NoMessagesWhenNothingPublishable(t *testing.T) {
	t.Parallel()

	// command is EMS-publish, so the simulator skips it in the telemetry path.
	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "command", Topic: "battery/x/command", Direction: "publish"},
		},
	}
	client := &fakeClient{}
	pub := mqtt.NewPublisher(client, "x", m)

	if err := pub.PublishSnapshot(context.Background(), model.TelemetrySnapshot{}); err != nil {
		t.Fatalf("publish: %v", err)
	}
	if len(client.messages) != 0 {
		t.Errorf("expected no messages, got %d", len(client.messages))
	}
}
