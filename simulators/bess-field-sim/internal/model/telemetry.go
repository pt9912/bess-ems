package model

import "time"

// TelemetrySnapshot is one tick of simulated field data, anchored to a
// scenario start time via OffsetMillis.
type TelemetrySnapshot struct {
	OffsetMillis       int64   `json:"offset_millis"`
	SocPercent         float64 `json:"soc_percent"`
	SohPercent         float64 `json:"soh_percent"`
	ActivePowerKw      float64 `json:"active_power_kw"`
	ReactivePowerKvar  float64 `json:"reactive_power_kvar"`
	DcVoltage          float64 `json:"dc_voltage"`
	DcCurrent          float64 `json:"dc_current"`
	TemperatureCelsius float64 `json:"temperature_celsius"`
	Available          bool    `json:"available"`
	FaultStatus        string  `json:"fault_status"`
}

// AbsoluteTime returns the wall-clock timestamp for this snapshot given a
// scenario start time. Time arithmetic only — no logic.
func (t TelemetrySnapshot) AbsoluteTime(start time.Time) time.Time {
	return start.Add(time.Duration(t.OffsetMillis) * time.Millisecond)
}
