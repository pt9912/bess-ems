package mqtt

import (
	"context"
	"fmt"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// MessageHandler receives one MQTT message. The handler runs on the
// transport's dispatcher goroutine; concurrent invocations for distinct
// messages are allowed, so handlers must be safe to call concurrently.
type MessageHandler func(topic string, payload []byte)

// Client is the broker-facing publish/subscribe surface the simulator
// depends on. Production wires a Paho or similar client; tests pass a
// mock. Direction note: on the wire, Subscribe binds to EMS-`publish`
// topics (e.g. command) and Publish emits EMS-`subscribe` topics (e.g.
// telemetry, status, fault, command_ack); see the package doc.
type Client interface {
	Publish(ctx context.Context, topic string, retained bool, payload []byte) error
	Subscribe(ctx context.Context, topic string, handler MessageHandler) error
}

// Publisher assembles MQTT messages from a TelemetrySnapshot and pushes
// them through Client. It owns no transport state — Connect/Close stay
// with the underlying Client implementation.
type Publisher struct {
	client  Client
	assetID string
	mapping model.MqttMapping
}

// NewPublisher constructs a Publisher for one asset and one mapping
// profile.
func NewPublisher(client Client, assetID string, mapping model.MqttMapping) *Publisher {
	return &Publisher{client: client, assetID: assetID, mapping: mapping}
}

// PublishSnapshot resolves and publishes every EMS-subscribe topic
// covered by the mapping (see package doc on direction semantics). The
// first publish error short-circuits the remaining topics.
func (p *Publisher) PublishSnapshot(ctx context.Context, snap model.TelemetrySnapshot) error {
	messages, err := ResolveTelemetry(snap, p.assetID, p.mapping)
	if err != nil {
		return err
	}
	for _, msg := range messages {
		if err := p.client.Publish(ctx, msg.Topic, msg.Retained, msg.Payload); err != nil {
			return fmt.Errorf("publish %q: %w", msg.Topic, err)
		}
	}
	return nil
}
