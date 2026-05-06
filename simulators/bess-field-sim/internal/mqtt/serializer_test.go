package mqtt_test

import (
	"encoding/json"
	"path/filepath"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/mqtt"
)

func TestSubstituteAssetID(t *testing.T) {
	t.Parallel()

	got := mqtt.SubstituteAssetID("battery/{assetId}/telemetry", "single-bess-1")
	want := "battery/single-bess-1/telemetry"
	if got != want {
		t.Errorf("got %q, want %q", got, want)
	}
}

func TestSubstituteAssetID_NoPlaceholderUnchanged(t *testing.T) {
	t.Parallel()

	got := mqtt.SubstituteAssetID("battery/x/telemetry", "ignored")
	if got != "battery/x/telemetry" {
		t.Errorf("got %q", got)
	}
}

func TestResolveTelemetry_SkipsEmsPublishTopics(t *testing.T) {
	t.Parallel()

	// direction is EMS-perspective; simulator publishes EMS-subscribe topics
	// only and ignores EMS-publish topics like `command`.
	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "command", Topic: "battery/{assetId}/command", Direction: "publish"},
			{Name: "telemetry", Topic: "battery/{assetId}/telemetry", Direction: "subscribe"},
		},
	}
	out, err := mqtt.ResolveTelemetry(model.TelemetrySnapshot{Available: true}, "x", m)
	if err != nil {
		t.Fatal(err)
	}
	if len(out) != 1 {
		t.Fatalf("expected 1 message, got %d", len(out))
	}
	if out[0].Topic != "battery/x/telemetry" {
		t.Errorf("topic: got %q", out[0].Topic)
	}
}

func TestResolveTelemetry_TelemetryPayloadCarriesSnapshot(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "telemetry", Topic: "battery/{assetId}/telemetry", Direction: "subscribe"},
		},
	}
	out, err := mqtt.ResolveTelemetry(model.TelemetrySnapshot{SocPercent: 60.5, Available: true}, "x", m)
	if err != nil {
		t.Fatal(err)
	}
	var decoded model.TelemetrySnapshot
	if err := json.Unmarshal(out[0].Payload, &decoded); err != nil {
		t.Fatalf("decode: %v", err)
	}
	if decoded.SocPercent != 60.5 {
		t.Errorf("soc: got %v", decoded.SocPercent)
	}
}

func TestResolveTelemetry_StatusPayloadIsFocused(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "status", Topic: "battery/{assetId}/status", Direction: "subscribe"},
		},
	}
	out, err := mqtt.ResolveTelemetry(model.TelemetrySnapshot{
		SocPercent: 60, Available: true, FaultStatus: "ok", OffsetMillis: 1000,
	}, "x", m)
	if err != nil {
		t.Fatal(err)
	}
	var payload map[string]any
	if err := json.Unmarshal(out[0].Payload, &payload); err != nil {
		t.Fatalf("decode: %v", err)
	}
	if _, has := payload["soc_percent"]; has {
		t.Error("status payload should not carry SOC")
	}
	if payload["available"] != true {
		t.Errorf("available: got %v", payload["available"])
	}
	if payload["fault_status"] != "ok" {
		t.Errorf("fault_status: got %v", payload["fault_status"])
	}
}

func TestResolveTelemetry_FaultTopicSkippedWhenStatusOk(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "fault", Topic: "battery/{assetId}/fault", Direction: "subscribe"},
		},
	}
	out, err := mqtt.ResolveTelemetry(model.TelemetrySnapshot{FaultStatus: "ok"}, "x", m)
	if err != nil {
		t.Fatal(err)
	}
	if len(out) != 0 {
		t.Errorf("expected no fault publish, got %d messages", len(out))
	}
}

func TestResolveTelemetry_FaultTopicEmittedOnNonOkStatus(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "fault", Topic: "battery/{assetId}/fault", Direction: "subscribe"},
		},
	}
	out, err := mqtt.ResolveTelemetry(model.TelemetrySnapshot{FaultStatus: "bms-overtemp"}, "x", m)
	if err != nil {
		t.Fatal(err)
	}
	if len(out) != 1 {
		t.Fatalf("expected 1 fault publish, got %d", len(out))
	}
	var payload map[string]any
	if err := json.Unmarshal(out[0].Payload, &payload); err != nil {
		t.Fatalf("decode: %v", err)
	}
	if payload["fault_status"] != "bms-overtemp" {
		t.Errorf("fault_status: got %v", payload["fault_status"])
	}
}

func TestResolveTelemetry_WithRealEmsPerspectiveMapping_PublishesSimulatorSide(t *testing.T) {
	t.Parallel()

	// Regression guard for the direction-semantics flip: the testdata file
	// is shipped EMS-perspective (direction="subscribe" for telemetry/status/
	// fault/command_ack, "publish" for command). The simulator must publish
	// telemetry+status+fault and stay silent on command and command_ack.
	m, err := mqtt.LoadMapping(filepath.Join("testdata", "mappings", "mqtt.simulator.json"))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	out, err := mqtt.ResolveTelemetry(model.TelemetrySnapshot{
		SocPercent: 50, Available: true, FaultStatus: "bms-overtemp", OffsetMillis: 1000,
	}, "single-bess-1", m)
	if err != nil {
		t.Fatal(err)
	}

	got := map[string]bool{}
	for _, msg := range out {
		got[msg.Topic] = true
	}
	for _, want := range []string{
		"battery/single-bess-1/telemetry",
		"battery/single-bess-1/status",
		"battery/single-bess-1/fault",
	} {
		if !got[want] {
			t.Errorf("simulator must publish %q", want)
		}
	}
	for _, forbidden := range []string{
		"battery/single-bess-1/command",
		"battery/single-bess-1/command/ack",
	} {
		if got[forbidden] {
			t.Errorf("simulator must not publish %q from telemetry path", forbidden)
		}
	}
}

func TestResolveTelemetry_UnknownTopicNameSkipped(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "totally_unknown", Topic: "battery/{assetId}/whatever", Direction: "subscribe"},
		},
	}
	out, err := mqtt.ResolveTelemetry(model.TelemetrySnapshot{}, "x", m)
	if err != nil {
		t.Fatal(err)
	}
	if len(out) != 0 {
		t.Errorf("expected no messages, got %d", len(out))
	}
}
