package model

// MqttMapping mirrors config/schema/mqtt-mapping.schema.json from the
// .NET-EMS. The simulator never duplicates EMS-Domain logic
// (plan-RM-M1-simulator.md §65); these structs carry shape only.
type MqttMapping struct {
	ProfileName string       `json:"profile_name"`
	Topics      []MqttTopic  `json:"topics"`
}

// MqttTopic mirrors a single topic entry in the MQTT mapping schema.
type MqttTopic struct {
	Name          string `json:"name"`
	Topic         string `json:"topic"`
	Direction     string `json:"direction"`
	PayloadFormat string `json:"payload_format"`
	Retained      bool   `json:"retained"`
	AuthRequired  string `json:"auth_required"`
}
