package mqtt_test

import (
	"errors"
	"path/filepath"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/mqtt"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/testroot"
)

func TestMain(m *testing.M) {
	testroot.Main(m)
}

func TestLoadMapping_Example(t *testing.T) {
	t.Parallel()

	m, err := mqtt.LoadMapping(repoMqttMapping(t, "mqtt.simulator.json"))
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if m.ProfileName != "mqtt-simulator" {
		t.Errorf("profile_name: got %q", m.ProfileName)
	}
	if len(m.Topics) == 0 {
		t.Fatal("topics empty")
	}
}

func TestLoadMapping_NotFound(t *testing.T) {
	t.Parallel()

	_, err := mqtt.LoadMapping("nonexistent/mqtt.json")
	if err == nil {
		t.Fatal("expected error for missing file")
	}
}

func TestLoadMapping_RejectsUnsafePath(t *testing.T) {
	t.Parallel()

	for _, path := range []string{"/nonexistent/mqtt.json", "../mqtt.json"} {
		_, err := mqtt.LoadMapping(path)
		if err == nil {
			t.Fatalf("expected error for unsafe path %q", path)
		}
	}
}

func TestParseMapping_RejectsMissingProfile(t *testing.T) {
	t.Parallel()

	_, err := mqtt.ParseMapping([]byte(`{"topics":[{"name":"t","topic":"x","direction":"publish","payload_format":"json","retained":false,"auth_required":"none"}]}`))
	if !errors.Is(err, mqtt.ErrMappingMissingProfile) {
		t.Fatalf("expected ErrMappingMissingProfile, got %v", err)
	}
}

func TestParseMapping_RejectsEmptyTopics(t *testing.T) {
	t.Parallel()

	_, err := mqtt.ParseMapping([]byte(`{"profile_name":"p","topics":[]}`))
	if !errors.Is(err, mqtt.ErrMappingNoTopics) {
		t.Fatalf("expected ErrMappingNoTopics, got %v", err)
	}
}

func TestParseMapping_RejectsTopicMissingName(t *testing.T) {
	t.Parallel()

	_, err := mqtt.ParseMapping([]byte(`{"profile_name":"p","topics":[{"topic":"x","direction":"publish","payload_format":"json","retained":false,"auth_required":"none"}]}`))
	if !errors.Is(err, mqtt.ErrTopicMissingName) {
		t.Fatalf("expected ErrTopicMissingName, got %v", err)
	}
}

func TestParseMapping_RejectsTopicMissingTopic(t *testing.T) {
	t.Parallel()

	_, err := mqtt.ParseMapping([]byte(`{"profile_name":"p","topics":[{"name":"t","direction":"publish","payload_format":"json","retained":false,"auth_required":"none"}]}`))
	if !errors.Is(err, mqtt.ErrTopicMissingTopic) {
		t.Fatalf("expected ErrTopicMissingTopic, got %v", err)
	}
}

func TestParseMapping_RejectsInvalidDirection(t *testing.T) {
	t.Parallel()

	_, err := mqtt.ParseMapping([]byte(`{"profile_name":"p","topics":[{"name":"t","topic":"x","direction":"shout","payload_format":"json","retained":false,"auth_required":"none"}]}`))
	if !errors.Is(err, mqtt.ErrTopicInvalidDirection) {
		t.Fatalf("expected ErrTopicInvalidDirection, got %v", err)
	}
}

func TestParseMapping_MalformedJSON(t *testing.T) {
	t.Parallel()

	_, err := mqtt.ParseMapping([]byte("not json"))
	if err == nil {
		t.Fatal("expected error")
	}
}

func TestLoadMapping_MalformedJSONOnDisk(t *testing.T) {
	t.Parallel()

	path := filepath.Join("testdata", "malformed", "mqtt-mapping.invalid-json")
	_, err := mqtt.LoadMapping(path)
	if err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateMapping_AcceptsSubscribeAndPublishMix(t *testing.T) {
	t.Parallel()

	m := model.MqttMapping{
		ProfileName: "p",
		Topics: []model.MqttTopic{
			{Name: "tel", Topic: "battery/x/telemetry", Direction: "subscribe"},
			{Name: "cmd", Topic: "battery/x/command", Direction: "publish"},
		},
	}
	if err := mqtt.ValidateMapping(m); err != nil {
		t.Fatalf("expected nil, got %v", err)
	}
}

func repoMqttMapping(t *testing.T, name string) string {
	t.Helper()
	return filepath.Join("testdata", "mappings", name)
}
