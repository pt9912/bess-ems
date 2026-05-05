package model

// ModbusMapping mirrors config/schema/modbus-mapping.schema.json from the
// .NET-EMS. Field shapes are authoritative on the .NET side; the simulator
// reproduces them as DTOs (plan-RM-M1-simulator.md §65).
type ModbusMapping struct {
	ProfileName     string           `json:"profile_name"`
	UnitIDDiscovery string           `json:"unit_id_discovery"`
	StaticUnitID    *int             `json:"static_unit_id,omitempty"`
	Registers       []ModbusRegister `json:"registers"`
}

// ModbusRegister mirrors a single register entry in the modbus mapping
// schema.
type ModbusRegister struct {
	Name               string            `json:"name"`
	Address            int               `json:"address"`
	Type               string            `json:"type"`
	ScaleFactor        float64           `json:"scale_factor"`
	Range              [2]float64        `json:"range"`
	Writable           bool              `json:"writable"`
	WriteCadence       string            `json:"write_cadence,omitempty"`
	AuthRequired       string            `json:"auth_required,omitempty"`
	Enum               map[string]string `json:"enum,omitempty"`
	FirmwareConstraint string            `json:"firmware_constraint,omitempty"`
	SunspecModel       *int              `json:"sunspec_model,omitempty"`
}
