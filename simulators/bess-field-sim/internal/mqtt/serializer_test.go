package mqtt_test

import (
	"encoding/json"
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

func TestResolveTelemetry_SkipsSubscribeTopics(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "command", Topic: "battery/{assetId}/command", Direction: "subscribe"},
			{Name: "telemetry", Topic: "battery/{assetId}/telemetry", Direction: "publish"},
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
			{Name: "telemetry", Topic: "battery/{assetId}/telemetry", Direction: "publish"},
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
			{Name: "status", Topic: "battery/{assetId}/status", Direction: "publish"},
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
			{Name: "fault", Topic: "battery/{assetId}/fault", Direction: "publish"},
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
			{Name: "fault", Topic: "battery/{assetId}/fault", Direction: "publish"},
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

func TestResolveTelemetry_UnknownTopicNameSkipped(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		Topics: []model.MqttTopic{
			{Name: "totally_unknown", Topic: "battery/{assetId}/whatever", Direction: "publish"},
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
