package mqtt

import (
	"context"
	"errors"
	"fmt"
	"net/url"
	"time"

	paho "github.com/eclipse/paho.mqtt.golang"
)

// PahoClient adapts github.com/eclipse/paho.mqtt.golang to the
// simulator's Client interface. Connect is performed in NewPahoClient
// so the cmd entrypoint can fail fast if the broker is unreachable.
type PahoClient struct {
	client paho.Client
	qos    byte
}

// NewPahoClient connects to brokerURL using clientID. The function
// blocks until the connection is established or the configured
// connect timeout elapses.
func NewPahoClient(brokerURL, clientID string) (*PahoClient, error) {
	if brokerURL == "" {
		return nil, errors.New("mqtt broker URL required")
	}
	parsedURL, err := url.Parse(brokerURL)
	if err != nil {
		return nil, fmt.Errorf("parse mqtt broker URL: %w", err)
	}
	if parsedURL.Scheme != "tcp" {
		return nil, fmt.Errorf("mqtt broker URL scheme %q is not supported; use tcp", parsedURL.Scheme)
	}
	// SECURITY: M1 simulator MQTT is anonymous plaintext only. Do not point
	// this client at production brokers until the M2 adapter hardening plan
	// (docs/plan/planning/in-progress/roadmap.md) adds TLS and credentials.
	// SetCleanSession plus AutoReconnect is intentional for replay-only test
	// runs: reconnects should not resume stale subscriptions or queued state.
	opts := paho.NewClientOptions().
		AddBroker(brokerURL).
		SetClientID(clientID).
		SetConnectTimeout(5 * time.Second).
		SetAutoReconnect(true).
		SetCleanSession(true)
	c := paho.NewClient(opts)
	token := c.Connect()
	if !token.WaitTimeout(5 * time.Second) {
		return nil, errors.New("mqtt connect timeout")
	}
	if err := token.Error(); err != nil {
		return nil, fmt.Errorf("mqtt connect: %w", err)
	}
	return &PahoClient{client: c, qos: 0}, nil
}

// Publish sends payload to topic. The call observes ctx cancellation
// independently of the underlying Paho token, so a cancelled run does
// not block on a slow broker.
func (p *PahoClient) Publish(ctx context.Context, topic string, retained bool, payload []byte) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	token := p.client.Publish(topic, p.qos, retained, payload)
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-token.Done():
		if err := token.Error(); err != nil {
			return fmt.Errorf("mqtt publish %q: %w", topic, err)
		}
		return nil
	}
}

// Subscribe registers handler for topic and waits for SUBACK. ctx gates
// the wait; once it returns successfully, Paho dispatches incoming
// messages to handler from its own goroutine until Close tears down the
// connection. SECURITY: M1 simulator MQTT is anonymous plaintext only;
// see NewPahoClient for the same caveat.
func (p *PahoClient) Subscribe(ctx context.Context, topic string, handler MessageHandler) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	cb := func(_ paho.Client, msg paho.Message) {
		handler(msg.Topic(), msg.Payload())
	}
	token := p.client.Subscribe(topic, p.qos, cb)
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-token.Done():
		if err := token.Error(); err != nil {
			return fmt.Errorf("mqtt subscribe %q: %w", topic, err)
		}
		return nil
	}
}

// Close disconnects from the broker, waiting up to 250ms for in-flight
// messages to drain.
func (p *PahoClient) Close() {
	p.client.Disconnect(250)
}
