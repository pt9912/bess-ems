package mqtt

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"time"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// ErrMappingNoCommandTopic is returned when the mapping has no
// EMS-`publish` command topic. The simulator subscribes to that topic
// to receive commands.
var ErrMappingNoCommandTopic = errors.New("mqtt mapping has no EMS-publish command topic")

// ErrMappingNoAckTopic is returned when the mapping has no
// EMS-`subscribe` command_ack topic. The simulator publishes ACKs on
// that topic.
var ErrMappingNoAckTopic = errors.New("mqtt mapping has no EMS-subscribe command_ack topic")

// Clock returns the current time. Tests inject a deterministic clock so
// CommandAck.DispatchedAt is reproducible.
type Clock func() time.Time

// CommandHandler subscribes to the command topic, decodes incoming JSON
// into model.Command, and immediately publishes an echo ACK on the
// command_ack topic. SIM-M1-11 specifies always-accepted ACKs to keep
// the EMS-side correlation test focused on CommandId matching; payload
// validation, latency variation, and rejection semantics belong to
// SIM-M1-12 and later stories.
//
// Malformed payloads (invalid JSON or missing command_id) are logged
// and dropped without an ACK so the EMS-side correlation surfaces a
// timeout — that keeps the contract symmetric: every well-formed
// command on the wire correlates exactly once.
type CommandHandler struct {
	client      Client
	cmdTopic    string
	ackTopic    string
	ackRetained bool
	clock       Clock
	logger      *slog.Logger
}

// NewCommandHandler resolves the command and command_ack topics from
// the mapping (substituting assetID into the templates) and returns a
// handler ready to Subscribe. Both topics are required; mappings that
// only ship telemetry must not pass through here.
func NewCommandHandler(client Client, assetID string, mapping model.MqttMapping, clock Clock, logger *slog.Logger) (*CommandHandler, error) {
	if clock == nil {
		clock = time.Now
	}
	if logger == nil {
		logger = slog.Default()
	}
	cmd, ok := findTopic(mapping, "command", "publish")
	if !ok {
		return nil, ErrMappingNoCommandTopic
	}
	ack, ok := findTopic(mapping, "command_ack", "subscribe")
	if !ok {
		return nil, ErrMappingNoAckTopic
	}
	return &CommandHandler{
		client:      client,
		cmdTopic:    SubstituteAssetID(cmd.Topic, assetID),
		ackTopic:    SubstituteAssetID(ack.Topic, assetID),
		ackRetained: ack.Retained,
		clock:       clock,
		logger:      logger,
	}, nil
}

// CommandTopic returns the broker-side command topic the handler binds
// to (with assetID already substituted). Useful for logs and tests.
func (h *CommandHandler) CommandTopic() string { return h.cmdTopic }

// AckTopic returns the broker-side command_ack topic ACKs land on.
func (h *CommandHandler) AckTopic() string { return h.ackTopic }

// Subscribe binds the command topic with the broker. ctx gates the
// SUBACK wait; once Subscribe returns, async messages dispatch to the
// internal handler. The handler closure captures ctx so a cancelled
// simulator stops emitting ACKs even if the broker keeps delivering
// messages on its end before Close completes.
func (h *CommandHandler) Subscribe(ctx context.Context) error {
	handler := func(topic string, payload []byte) {
		h.handle(ctx, topic, payload)
	}
	if err := h.client.Subscribe(ctx, h.cmdTopic, handler); err != nil {
		return fmt.Errorf("subscribe %q: %w", h.cmdTopic, err)
	}
	return nil
}

func (h *CommandHandler) handle(ctx context.Context, topic string, payload []byte) {
	if err := ctx.Err(); err != nil {
		return
	}
	var cmd model.Command
	if err := json.Unmarshal(payload, &cmd); err != nil {
		h.logger.Warn("mqtt: drop malformed command payload",
			"topic", topic, "error", err)
		return
	}
	if cmd.CommandID == "" {
		h.logger.Warn("mqtt: drop command without command_id",
			"topic", topic)
		return
	}
	ack := model.CommandAck{
		CommandID:    cmd.CommandID,
		Accepted:     true,
		DispatchedAt: h.clock(),
		Reason:       "accepted",
	}
	body, err := json.Marshal(ack)
	if err != nil {
		h.logger.Error("mqtt: encode ack",
			"command_id", cmd.CommandID, "error", err)
		return
	}
	if err := h.client.Publish(ctx, h.ackTopic, h.ackRetained, body); err != nil {
		h.logger.Error("mqtt: publish ack",
			"command_id", cmd.CommandID, "error", err)
	}
}

func findTopic(mapping model.MqttMapping, name, direction string) (model.MqttTopic, bool) {
	for _, t := range mapping.Topics {
		if t.Name == name && t.Direction == direction {
			return t, true
		}
	}
	return model.MqttTopic{}, false
}
