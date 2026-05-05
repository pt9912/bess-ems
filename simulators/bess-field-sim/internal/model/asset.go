// Package model carries the JSON-shaped data transfer objects exchanged
// between scenario fixtures and protocol adapters. The package owns no
// behaviour beyond struct definitions; per plan-RM-M1-simulator.md §65 the
// simulator never duplicates EMS-Domain logic.
package model

// BatteryAsset mirrors config/schema/asset.schema.json from the .NET-EMS.
// Field shapes and ranges are authoritative on the .NET side.
type BatteryAsset struct {
	AssetID                        string  `json:"asset_id"`
	CapacityKwh                    float64 `json:"capacity_kwh"`
	MaxChargePowerKw               float64 `json:"max_charge_power_kw"`
	MaxDischargePowerKw            float64 `json:"max_discharge_power_kw"`
	MinSocPercent                  float64 `json:"min_soc_percent"`
	MaxSocPercent                  float64 `json:"max_soc_percent"`
	ChargeEfficiency               float64 `json:"charge_efficiency"`
	DischargeEfficiency            float64 `json:"discharge_efficiency"`
	MaxRampKwPerSecond             float64 `json:"max_ramp_kw_per_second"`
	MinOperatingTemperatureCelsius float64 `json:"min_operating_temperature_celsius"`
	MaxOperatingTemperatureCelsius float64 `json:"max_operating_temperature_celsius"`
}
