// Package mqtt holds the simulator's MQTT publish/subscribe surface and
// the pure logic that turns a TelemetrySnapshot into MQTT topic payloads.
// The actual broker connection (Paho or any other transport) is injected
// via Client so unit tests can run without a real Mosquitto.
package mqtt

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/safepath"
)

var (
	// ErrMappingMissingProfile is returned when profile_name is missing.
	ErrMappingMissingProfile = errors.New("mqtt mapping profile_name missing")
	// ErrMappingNoTopics is returned when topics list is empty.
	ErrMappingNoTopics = errors.New("mqtt mapping has no topics")
	// ErrTopicMissingName is returned when a topic entry has no name.
	ErrTopicMissingName = errors.New("mqtt topic name missing")
	// ErrTopicMissingTopic is returned when a topic entry has no topic.
	ErrTopicMissingTopic = errors.New("mqtt topic topic missing")
	// ErrTopicInvalidDirection is returned when direction is not subscribe or publish.
	ErrTopicInvalidDirection = errors.New("mqtt topic direction must be subscribe or publish")
)

// LoadMapping reads an MqttMapping fixture from disk and validates it.
func LoadMapping(path string) (model.MqttMapping, error) {
	cleanPath, err := safepath.CleanRelative(path)
	if err != nil {
		return model.MqttMapping{}, fmt.Errorf("validate mapping path: %w", err)
	}
	data, err := os.ReadFile(cleanPath)
	if err != nil {
		return model.MqttMapping{}, fmt.Errorf("read mapping %q: %w", path, err)
	}
	return ParseMapping(data)
}

// ParseMapping decodes and validates an MqttMapping from raw JSON.
func ParseMapping(data []byte) (model.MqttMapping, error) {
	var m model.MqttMapping
	if err := json.Unmarshal(data, &m); err != nil {
		return model.MqttMapping{}, fmt.Errorf("decode mapping: %w", err)
	}
	if err := ValidateMapping(m); err != nil {
		return model.MqttMapping{}, err
	}
	return m, nil
}

// ValidateMapping checks structural invariants. Domain invariants stay on
// the .NET-EMS side (config/schema/mqtt-mapping.schema.json).
func ValidateMapping(m model.MqttMapping) error {
	if m.ProfileName == "" {
		return ErrMappingMissingProfile
	}
	if len(m.Topics) == 0 {
		return ErrMappingNoTopics
	}
	for i, topic := range m.Topics {
		if topic.Name == "" {
			return fmt.Errorf("%w: index %d", ErrTopicMissingName, i)
		}
		if topic.Topic == "" {
			return fmt.Errorf("%w: index %d", ErrTopicMissingTopic, i)
		}
		if topic.Direction != "subscribe" && topic.Direction != "publish" {
			return fmt.Errorf("%w: index %d direction=%q", ErrTopicInvalidDirection, i, topic.Direction)
		}
	}
	return nil
}
