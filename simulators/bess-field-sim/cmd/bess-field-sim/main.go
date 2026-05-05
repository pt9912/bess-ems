// Command bess-field-sim loads a scenario fixture and (in later
// commits) drives Modbus/MQTT servers so the .NET-EMS adapters have a
// deterministic counterparty. plan-RM-M1-simulator.md §27 keeps the
// simulator a black-box service; this entrypoint is the only place
// allowed to wire packages together.
package main

import (
	"fmt"
	"log/slog"
	"os"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/scenario"
)

const usage = "usage: bess-field-sim <scenario-file>"

func main() {
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, usage)
		os.Exit(2)
	}

	scn, err := scenario.LoadFromFile(os.Args[1])
	if err != nil {
		slog.Error("failed to load scenario", "error", err)
		os.Exit(1)
	}

	slog.Info("scenario loaded",
		"id", scn.ID,
		"name", scn.Name,
		"asset", scn.Asset.AssetID,
		"telemetry_ticks", len(scn.Telemetry),
	)
}
