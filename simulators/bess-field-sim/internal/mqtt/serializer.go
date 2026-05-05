package mqtt

import (
	"encoding/json"
	"fmt"
	"strings"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// Resolved is one MQTT message ready to publish: the placeholder-filled
// topic, the encoded payload, and the retained flag from the mapping.
type Resolved struct {
	Topic    string
	Payload  []byte
	Retained bool
}

// ResolveTelemetry returns the publish-direction Resolved messages for
// the given snapshot, with `{assetId}` placeholders substituted. The
// telemetry topic carries a JSON encoding of the snapshot; status and
// fault topics use focused subsets so subscribers do not have to parse
// every field every tick.
func ResolveTelemetry(snap model.TelemetrySnapshot, assetID string, mapping model.MqttMapping) ([]Resolved, error) {
	out := make([]Resolved, 0, len(mapping.Topics))
	for _, topic := range mapping.Topics {
		if topic.Direction != "publish" {
			continue
		}
		payload, err := payloadFor(topic.Name, snap)
		if err != nil {
			return nil, err
		}
		if payload == nil {
			continue
		}
		out = append(out, Resolved{
			Topic:    SubstituteAssetID(topic.Topic, assetID),
			Payload:  payload,
			Retained: topic.Retained,
		})
	}
	return out, nil
}

// SubstituteAssetID replaces every `{assetId}` placeholder with id.
func SubstituteAssetID(topic, id string) string {
	return strings.ReplaceAll(topic, "{assetId}", id)
}

func payloadFor(name string, snap model.TelemetrySnapshot) ([]byte, error) {
	switch name {
	case "telemetry":
		return marshalJSON(snap)
	case "status":
		return marshalJSON(map[string]any{
			"available":     snap.Available,
			"fault_status":  snap.FaultStatus,
			"offset_millis": snap.OffsetMillis,
		})
	case "fault":
		if snap.FaultStatus == "" || snap.FaultStatus == "ok" {
			return nil, nil
		}
		return marshalJSON(map[string]any{
			"fault_status":  snap.FaultStatus,
			"offset_millis": snap.OffsetMillis,
		})
	default:
		return nil, nil
	}
}

func marshalJSON(v any) ([]byte, error) {
	b, err := json.Marshal(v)
	if err != nil {
		return nil, fmt.Errorf("marshal payload: %w", err)
	}
	return b, nil
}
