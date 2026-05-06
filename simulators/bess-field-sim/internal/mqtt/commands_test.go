package mqtt_test

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"testing"
	"time"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/mqtt"
)

func validCommandMapping() model.MqttMapping {
	return model.MqttMapping{
		ProfileName: "p",
		Topics: []model.MqttTopic{
			{Name: "command", Topic: "battery/{assetId}/command", Direction: "publish"},
			{Name: "command_ack", Topic: "battery/{assetId}/command/ack", Direction: "subscribe", Retained: false},
		},
	}
}

func quietLogger() *slog.Logger {
	return slog.New(slog.NewTextHandler(io.Discard, nil))
}

func fixedClock(t time.Time) mqtt.Clock {
	return func() time.Time { return t }
}

func TestNewCommandHandler_ResolvesAndSubstitutesTopics(t *testing.T) {
	t.Parallel()

	h, err := mqtt.NewCommandHandler(&fakeClient{}, "single-bess-1", validCommandMapping(), nil, quietLogger())
	if err != nil {
		t.Fatalf("new: %v", err)
	}
	if got, want := h.CommandTopic(), "battery/single-bess-1/command"; got != want {
		t.Errorf("command topic: got %q want %q", got, want)
	}
	if got, want := h.AckTopic(), "battery/single-bess-1/command/ack"; got != want {
		t.Errorf("ack topic: got %q want %q", got, want)
	}
}

func TestNewCommandHandler_RejectsMappingWithoutCommandTopic(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		ProfileName: "p",
		Topics: []model.MqttTopic{
			{Name: "command_ack", Topic: "battery/x/command/ack", Direction: "subscribe"},
		},
	}
	_, err := mqtt.NewCommandHandler(&fakeClient{}, "x", m, nil, quietLogger())
	if !errors.Is(err, mqtt.ErrMappingNoCommandTopic) {
		t.Fatalf("expected ErrMappingNoCommandTopic, got %v", err)
	}
}

func TestNewCommandHandler_RejectsMappingWithoutAckTopic(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		ProfileName: "p",
		Topics: []model.MqttTopic{
			{Name: "command", Topic: "battery/x/command", Direction: "publish"},
		},
	}
	_, err := mqtt.NewCommandHandler(&fakeClient{}, "x", m, nil, quietLogger())
	if !errors.Is(err, mqtt.ErrMappingNoAckTopic) {
		t.Fatalf("expected ErrMappingNoAckTopic, got %v", err)
	}
}

func TestNewCommandHandler_RejectsAckTopicWithWrongDirection(t *testing.T) {
	t.Parallel()

	// direction is EMS-perspective; command_ack with direction="publish"
	// would mean the EMS publishes ACKs, which is nonsense. The handler
	// must refuse the mapping rather than silently emit on the wrong side.
	m := model.MqttMapping{
		ProfileName: "p",
		Topics: []model.MqttTopic{
			{Name: "command", Topic: "battery/x/command", Direction: "publish"},
			{Name: "command_ack", Topic: "battery/x/command/ack", Direction: "publish"},
		},
	}
	_, err := mqtt.NewCommandHandler(&fakeClient{}, "x", m, nil, quietLogger())
	if !errors.Is(err, mqtt.ErrMappingNoAckTopic) {
		t.Fatalf("expected ErrMappingNoAckTopic, got %v", err)
	}
}

func TestCommandHandler_Subscribe_RegistersResolvedTopic(t *testing.T) {
	t.Parallel()

	client := &fakeClient{}
	h, err := mqtt.NewCommandHandler(client, "single-bess-1", validCommandMapping(), nil, quietLogger())
	if err != nil {
		t.Fatalf("new: %v", err)
	}
	if err := h.Subscribe(context.Background()); err != nil {
		t.Fatalf("subscribe: %v", err)
	}
	if len(client.subscriptions) != 1 {
		t.Fatalf("expected 1 subscription, got %d", len(client.subscriptions))
	}
	if got := client.subscriptions[0].topic; got != "battery/single-bess-1/command" {
		t.Errorf("subscribed topic: got %q", got)
	}
}

func TestCommandHandler_Subscribe_SurfacesTransportError(t *testing.T) {
	t.Parallel()

	client := &fakeClient{subscribeReturn: errors.New("broker rejected sub")}
	h, err := mqtt.NewCommandHandler(client, "x", validCommandMapping(), nil, quietLogger())
	if err != nil {
		t.Fatalf("new: %v", err)
	}
	if err := h.Subscribe(context.Background()); err == nil {
		t.Fatal("expected subscribe error to surface")
	}
}

func TestCommandHandler_ValidCommand_PublishesEchoAck(t *testing.T) {
	t.Parallel()

	dispatchedAt := time.Date(2026, time.May, 6, 9, 30, 0, 0, time.UTC)
	client := &fakeClient{}
	h, err := mqtt.NewCommandHandler(client, "single-bess-1", validCommandMapping(), fixedClock(dispatchedAt), quietLogger())
	if err != nil {
		t.Fatalf("new: %v", err)
	}
	if err := h.Subscribe(context.Background()); err != nil {
		t.Fatalf("subscribe: %v", err)
	}

	cmdJSON, _ := json.Marshal(model.Command{
		CommandID:     "cmd-42",
		AssetID:       "single-bess-1",
		Mode:          "Discharge",
		ActivePowerKw: 25,
	})
	if !client.deliver("battery/single-bess-1/command", cmdJSON) {
		t.Fatal("no subscription registered")
	}

	msgs := client.capturedMessages()
	if len(msgs) != 1 {
		t.Fatalf("expected 1 ack publish, got %d", len(msgs))
	}
	if got, want := msgs[0].topic, "battery/single-bess-1/command/ack"; got != want {
		t.Errorf("ack topic: got %q want %q", got, want)
	}
	if msgs[0].retained {
		t.Error("ack should not be retained when mapping says retained=false")
	}
	var ack model.CommandAck
	if err := json.Unmarshal(msgs[0].payload, &ack); err != nil {
		t.Fatalf("decode ack: %v", err)
	}
	if ack.CommandID != "cmd-42" {
		t.Errorf("ack command_id: got %q", ack.CommandID)
	}
	if !ack.Accepted {
		t.Error("ack must be accepted=true (SIM-M1-11 echo policy)")
	}
	if ack.Reason != "accepted" {
		t.Errorf("ack reason: got %q want %q", ack.Reason, "accepted")
	}
	if !ack.DispatchedAt.Equal(dispatchedAt) {
		t.Errorf("dispatched_at: got %v want %v", ack.DispatchedAt, dispatchedAt)
	}
}

func TestCommandHandler_RetainedFlagPropagatedFromAckMapping(t *testing.T) {
	t.Parallel()

	m := validCommandMapping()
	m.Topics[1].Retained = true
	client := &fakeClient{}
	h, err := mqtt.NewCommandHandler(client, "x", m, nil, quietLogger())
	if err != nil {
		t.Fatalf("new: %v", err)
	}
	if err := h.Subscribe(context.Background()); err != nil {
		t.Fatalf("subscribe: %v", err)
	}
	cmdJSON, _ := json.Marshal(model.Command{CommandID: "cmd-7"})
	client.deliver("battery/x/command", cmdJSON)

	msgs := client.capturedMessages()
	if len(msgs) != 1 || !msgs[0].retained {
		t.Fatalf("expected retained ack, got %+v", msgs)
	}
}

func TestCommandHandler_MalformedPayload_NoAck(t *testing.T) {
	t.Parallel()

	client := &fakeClient{}
	h, _ := mqtt.NewCommandHandler(client, "x", validCommandMapping(), nil, quietLogger())
	_ = h.Subscribe(context.Background())

	client.deliver("battery/x/command", []byte("not json"))

	if got := len(client.capturedMessages()); got != 0 {
		t.Errorf("expected no ack for malformed payload, got %d publishes", got)
	}
}

func TestCommandHandler_MissingCommandId_NoAck(t *testing.T) {
	t.Parallel()

	client := &fakeClient{}
	h, _ := mqtt.NewCommandHandler(client, "x", validCommandMapping(), nil, quietLogger())
	_ = h.Subscribe(context.Background())

	cmdJSON, _ := json.Marshal(model.Command{Mode: "Stop"})
	client.deliver("battery/x/command", cmdJSON)

	if got := len(client.capturedMessages()); got != 0 {
		t.Errorf("expected no ack for missing command_id, got %d publishes", got)
	}
}

func TestCommandHandler_CancelledContext_DropsAck(t *testing.T) {
	t.Parallel()

	client := &fakeClient{}
	h, _ := mqtt.NewCommandHandler(client, "x", validCommandMapping(), nil, quietLogger())
	ctx, cancel := context.WithCancel(context.Background())
	if err := h.Subscribe(ctx); err != nil {
		t.Fatalf("subscribe: %v", err)
	}
	cancel()

	cmdJSON, _ := json.Marshal(model.Command{CommandID: "cmd-after-cancel"})
	client.deliver("battery/x/command", cmdJSON)

	if got := len(client.capturedMessages()); got != 0 {
		t.Errorf("cancelled handler should drop, got %d publishes", got)
	}
}

func TestCommandHandler_DefaultClockUsed_WhenNilProvided(t *testing.T) {
	t.Parallel()

	client := &fakeClient{}
	h, err := mqtt.NewCommandHandler(client, "x", validCommandMapping(), nil, quietLogger())
	if err != nil {
		t.Fatalf("new: %v", err)
	}
	_ = h.Subscribe(context.Background())

	before := time.Now()
	cmdJSON, _ := json.Marshal(model.Command{CommandID: "cmd-clk"})
	client.deliver("battery/x/command", cmdJSON)
	after := time.Now()

	msgs := client.capturedMessages()
	if len(msgs) != 1 {
		t.Fatalf("expected 1 ack, got %d", len(msgs))
	}
	var ack model.CommandAck
	_ = json.Unmarshal(msgs[0].payload, &ack)
	if ack.DispatchedAt.Before(before) || ack.DispatchedAt.After(after) {
		t.Errorf("default clock not used: dispatched_at=%v outside [%v,%v]",
			ack.DispatchedAt, before, after)
	}
}

func TestCommandHandler_DefaultLoggerUsed_WhenNilProvided(t *testing.T) {
	t.Parallel()

	// Constructing with a nil logger must not panic; the handler falls
	// back to slog.Default() so production never silently swallows
	// malformed-payload diagnostics.
	client := &fakeClient{}
	h, err := mqtt.NewCommandHandler(client, "x", validCommandMapping(), fixedClock(time.Now()), nil)
	if err != nil {
		t.Fatalf("new: %v", err)
	}
	if err := h.Subscribe(context.Background()); err != nil {
		t.Fatalf("subscribe: %v", err)
	}
	// Drive a malformed payload to exercise the warn path under the default logger.
	client.deliver("battery/x/command", []byte("not json"))
}
