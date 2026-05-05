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
	mu       sync.Mutex
	messages []capturedMessage
	failOn   string
}

type capturedMessage struct {
	topic    string
	retained bool
	payload  []byte
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

func TestPublisher_PublishSnapshot_PublishesPublishDirectionTopics(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		ProfileName: "p",
		Topics: []model.MqttTopic{
			{Name: "telemetry", Topic: "battery/{assetId}/telemetry", Direction: "publish", Retained: false},
			{Name: "status", Topic: "battery/{assetId}/status", Direction: "publish", Retained: true},
			{Name: "command", Topic: "battery/{assetId}/command", Direction: "subscribe"},
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
			{Name: "telemetry", Topic: "battery/x/telemetry", Direction: "publish"},
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

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "command", Topic: "battery/x/command", Direction: "subscribe"},
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
