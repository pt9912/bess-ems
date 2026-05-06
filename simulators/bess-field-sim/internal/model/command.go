package model

import "time"

// Command mirrors BatteryEms.Domain.BatteryCommand on the wire. The
// simulator never duplicates EMS-Domain logic
// (plan-RM-M1-simulator.md §65); these structs carry shape only. Mode
// and Source carry the .NET enum names verbatim so the wire format
// matches what System.Text.Json's JsonStringEnumConverter emits:
// Mode   ∈ {"Stop","Charge","Discharge","Idle"}
// Source ∈ {"Schedule","Operator","RegelLeistung","Safety",
//           "Optimization","Fallback"}.
//
// SIM-M1-11 only needs CommandID for ACK correlation; the remaining
// fields are decoded for forward compatibility (SIM-M1-12 limit
// rejection, observability).
type Command struct {
	CommandID         string    `json:"command_id"`
	Timestamp         time.Time `json:"timestamp"`
	AssetID           string    `json:"asset_id"`
	Mode              string    `json:"mode"`
	ActivePowerKw     float64   `json:"active_power_kw"`
	ReactivePowerKvar *float64  `json:"reactive_power_kvar,omitempty"`
	ValidUntil        time.Time `json:"valid_until"`
	Reason            string    `json:"reason,omitempty"`
	Source            string    `json:"source,omitempty"`
}

// CommandAck is the acknowledgment payload the simulator publishes on
// the EMS-`subscribe` command_ack topic. SIM-M1-11 specifies an
// always-accepted echo ACK so the EMS adapter can validate Correlation
// over CommandId without timing or rejection variability bleeding into
// the test signal. Malformed Commands (invalid JSON or missing
// command_id) are dropped without an ACK so the EMS surfaces a timeout
// instead of a false-positive correlation hit.
type CommandAck struct {
	CommandID    string    `json:"command_id"`
	Accepted     bool      `json:"accepted"`
	DispatchedAt time.Time `json:"dispatched_at"`
	Reason       string    `json:"reason,omitempty"`
}
