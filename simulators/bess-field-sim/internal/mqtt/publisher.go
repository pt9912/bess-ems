package mqtt

import (
	"context"
	"fmt"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// Client is the broker-facing publish surface the simulator depends on.
// Production wires a Paho or similar client; tests pass a mock.
type Client interface {
	Publish(ctx context.Context, topic string, retained bool, payload []byte) error
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
