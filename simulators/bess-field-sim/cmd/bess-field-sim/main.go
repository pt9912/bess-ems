// Command bess-field-sim drives a deterministic field-simulation
// scenario over Modbus TCP and MQTT so the .NET-EMS adapters have a
// reproducible counterparty (plan-RM-M1-simulator.md §27). The cmd is
// the only place that wires runtime + modbus + mqtt + paho together.
package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/modbus"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/mqtt"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/runtime"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/scenario"
)

const defaultModbusAddr = ":5020"

type cliFlags struct {
	scenarioPath    string
	modbusMapping   string
	modbusAddr      string
	mqttMapping     string
	mqttBroker      string
	mqttClientID    string
	assetIDOverride string
}

type outputs struct {
	modbus *modbus.Server
	mqtt   *mqtt.Publisher
	client *mqtt.PahoClient
}

func parseFlags(args []string) (cliFlags, error) {
	fs := flag.NewFlagSet("bess-field-sim", flag.ContinueOnError)
	var f cliFlags
	fs.StringVar(&f.scenarioPath, "scenario", "", "path to scenario fixture (required)")
	fs.StringVar(&f.modbusMapping, "modbus-mapping", "", "path to modbus mapping JSON; enables Modbus TCP server when set")
	fs.StringVar(&f.modbusAddr, "modbus-addr", defaultModbusAddr, "Modbus TCP listen address")
	fs.StringVar(&f.mqttMapping, "mqtt-mapping", "", "path to MQTT mapping JSON; enables MQTT publishing when set together with -mqtt-broker")
	fs.StringVar(&f.mqttBroker, "mqtt-broker", "", "MQTT broker URL (e.g. tcp://localhost:1883)")
	fs.StringVar(&f.mqttClientID, "mqtt-client-id", "bess-field-sim", "MQTT client identifier")
	fs.StringVar(&f.assetIDOverride, "asset-id", "", "override asset_id from scenario for MQTT topic substitution")
	if err := fs.Parse(args); err != nil {
		return cliFlags{}, fmt.Errorf("parse flags: %w", err)
	}
	if f.scenarioPath == "" {
		return cliFlags{}, errors.New("-scenario is required")
	}
	return f, nil
}

func run(args []string) error {
	flags, err := parseFlags(args)
	if err != nil {
		return err
	}

	scn, err := scenario.LoadFromFile(flags.scenarioPath)
	if err != nil {
		return fmt.Errorf("load scenario: %w", err)
	}
	slog.Info("scenario loaded",
		"id", scn.ID,
		"name", scn.Name,
		"asset", scn.Asset.AssetID,
		"telemetry_ticks", len(scn.Telemetry),
	)

	ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	var io outputs
	defer func() {
		// Shutdown order is intentional: cancel the orchestrator context before
		// transport drains so in-flight publishes observe cancellation first.
		cancel()
		if io.client != nil {
			io.client.Close()
		}
		if io.modbus != nil {
			io.modbus.Close()
		}
	}()

	io.modbus, err = startModbus(flags)
	if err != nil {
		return err
	}
	io.mqtt, io.client, err = startMQTT(flags, scn.Asset.AssetID)
	if err != nil {
		return err
	}

	if !io.hasIO() {
		slog.Warn("no Modbus mapping and no MQTT mapping/broker — running scenario without IO")
	}

	o := io.orchestrator()
	if err := o.Run(ctx, scn); err != nil {
		if errors.Is(err, context.Canceled) {
			slog.Info("scenario canceled", "id", scn.ID)
			return nil
		}
		return fmt.Errorf("run scenario: %w", err)
	}
	slog.Info("scenario complete", "id", scn.ID)
	return nil
}

func (o outputs) hasIO() bool {
	return o.modbus != nil || o.mqtt != nil
}

func (o outputs) orchestrator() *runtime.Orchestrator {
	switch {
	case o.modbus != nil && o.mqtt != nil:
		return runtime.NewOrchestrator(o.modbus, o.mqtt, runtime.SleepWithContext)
	case o.modbus != nil:
		return runtime.NewOrchestrator(o.modbus, nil, runtime.SleepWithContext)
	case o.mqtt != nil:
		return runtime.NewOrchestrator(nil, o.mqtt, runtime.SleepWithContext)
	default:
		return runtime.NewOrchestrator(nil, nil, runtime.SleepWithContext)
	}
}

func startModbus(flags cliFlags) (*modbus.Server, error) {
	if flags.modbusMapping == "" {
		if flags.modbusAddr != defaultModbusAddr {
			slog.Warn("modbus address ignored because no Modbus mapping was provided", "addr", flags.modbusAddr)
		}
		return nil, nil
	}

	mapping, err := modbus.LoadMapping(flags.modbusMapping)
	if err != nil {
		return nil, fmt.Errorf("load modbus mapping: %w", err)
	}
	server := modbus.NewServer(mapping)
	if err := server.ListenTCP(flags.modbusAddr); err != nil {
		return nil, fmt.Errorf("listen modbus on %s: %w", flags.modbusAddr, err)
	}
	slog.Info("modbus server listening", "addr", flags.modbusAddr, "profile", mapping.ProfileName)
	return server, nil
}

func startMQTT(flags cliFlags, scenarioAssetID string) (*mqtt.Publisher, *mqtt.PahoClient, error) {
	if flags.mqttMapping == "" || flags.mqttBroker == "" {
		return nil, nil, nil
	}

	mapping, err := mqtt.LoadMapping(flags.mqttMapping)
	if err != nil {
		return nil, nil, fmt.Errorf("load mqtt mapping: %w", err)
	}
	client, err := mqtt.NewPahoClient(flags.mqttBroker, flags.mqttClientID)
	if err != nil {
		return nil, nil, fmt.Errorf("connect mqtt: %w", err)
	}
	assetID := scenarioAssetID
	if flags.assetIDOverride != "" {
		assetID = flags.assetIDOverride
	}
	publisher := mqtt.NewPublisher(client, assetID, mapping)
	slog.Info("mqtt publisher ready", "broker", flags.mqttBroker, "client_id", flags.mqttClientID, "asset", assetID)
	return publisher, client, nil
}

func main() {
	if err := run(os.Args[1:]); err != nil {
		slog.Error("bess-field-sim failed", "error", err)
		os.Exit(1)
	}
}
