package mqtt

import (
	"context"
	"errors"
	"fmt"
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
	token := p.client.Publish(topic, p.qos, retained, payload)
	done := make(chan struct{})
	go func() {
		token.Wait()
		close(done)
	}()
	select {
	case <-ctx.Done():
		return ctx.Err() //nolint:wrapcheck // standard cancellation contract
	case <-done:
		if err := token.Error(); err != nil {
			return fmt.Errorf("mqtt publish %q: %w", topic, err)
		}
		return nil
	}
}

// Close disconnects from the broker, waiting up to 250ms for in-flight
// messages to drain.
func (p *PahoClient) Close() {
	p.client.Disconnect(250)
}
