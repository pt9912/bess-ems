package model

// Scenario is a deterministic field-simulation case per
// plan-RM-M1-simulator.md §136. It bundles the asset under test with a
// time-ordered telemetry sequence the simulator replays at runtime.
type Scenario struct {
	ID          string              `json:"id"`
	Name        string              `json:"name"`
	Description string              `json:"description"`
	Asset       BatteryAsset        `json:"asset"`
	Telemetry   []TelemetrySnapshot `json:"telemetry"`
}
